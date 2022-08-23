﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using AWSRedrive.Interfaces;
using Newtonsoft.Json.Linq;
using NLog;
using RestSharp;
using RestSharp.Authenticators;

namespace AWSRedrive
{
    public class HttpMessageProcessor : IMessageProcessor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry)
        {
            Logger.Trace($"Preparing request to {configurationEntry.RedriveUrl}");
            var uri = new Uri(configurationEntry.RedriveUrl);

            var options = CreateOptions(uri, configurationEntry);

            var client = new RestClient(options);

            var request = CreateRequest(message, uri, configurationEntry);

            AddAuthentication(client, request, configurationEntry);

            AddAttributes(request, attributes);

            SendRequest(client, request, configurationEntry);
        }

        private RestRequest CreateRequest(string message, Uri uri, ConfigurationEntry configurationEntry)
        {
            return !configurationEntry.UseGET 
                ? CreatePostOrPutOrDeleteRequest(message, uri, configurationEntry) 
                : CreateGetRequest(message, uri);
        }

        private RestRequest CreateGetRequest(string message, Uri uri)
        {
            var request = new RestRequest(uri.PathAndQuery, Method.Get);
            try
            {
                var data = JObject.Parse(message);
                foreach (var p in data.Properties())
                {
                    request.AddQueryParameter(p.Name, p.Value.ToString());
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error parsing message and adding query parameters. GET request might be incorrect.");
                Logger.Warn($"Message was [{message}]");
            }

            return request;
        }

        private RestRequest CreatePostOrPutOrDeleteRequest(string message, Uri uri, ConfigurationEntry configurationEntry)
        {
            var request = new RestRequest(uri.PathAndQuery, configurationEntry.UseDelete ? Method.Delete : configurationEntry.UsePUT ? Method.Put : Method.Post);

            request.AddStringBody(message, DataFormat.Json);

            return request;
        }

        private RestClientOptions CreateOptions(Uri uri, ConfigurationEntry configurationEntry)
        {
            var options = new RestClientOptions($"{uri.Scheme}://{uri.Host}:{uri.Port}");

            if (configurationEntry.IgnoreCertificateErrors)
            {
                options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }

            if (configurationEntry.Timeout.HasValue)
            {
                options.Timeout = configurationEntry.Timeout.Value;
            }

            return options;
        }

        private void AddAuthentication(RestClient client, RestRequest request, ConfigurationEntry configurationEntry)
        {
            if (!string.IsNullOrEmpty(configurationEntry.AwsGatewayToken))
            {
                request.AddHeader("x-api-key", configurationEntry.AwsGatewayToken);
            }

            if (!string.IsNullOrEmpty(configurationEntry.AuthToken))
            {
                request.AddHeader("Authorization", configurationEntry.AuthToken);
            }

            if (!string.IsNullOrEmpty(configurationEntry.BasicAuthPassword) &&
                !string.IsNullOrEmpty(configurationEntry.BasicAuthUserName))
            {
                client.Authenticator = new HttpBasicAuthenticator(configurationEntry.BasicAuthUserName,
                    configurationEntry.BasicAuthPassword);
            }
        }

        private void AddAttributes(RestRequest request, Dictionary<string, string> attributes)
        {
            if (attributes != null)
            {
                foreach (var key in attributes.Keys.Where(key => !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(attributes[key])))
                {
                    request.AddHeader(key, attributes[key]);
                }
            }
        }

        private void SendRequest(RestClient client, RestRequest request, ConfigurationEntry configurationEntry)
        {
            Logger.Trace($"Posting to {configurationEntry.RedriveUrl}");
            var response = client.ExecuteAsync(request).Result;

            if (response.IsSuccessful &&
                (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created))
            {
                Logger.Trace($"Post to {configurationEntry.RedriveUrl} successful");
                return;
            }

            Logger.Trace($"Post to {configurationEntry.RedriveUrl} failed (status code [{response.StatusCode}], error [{response.ErrorMessage}])");
            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            throw new InvalidOperationException($"Received {response.StatusCode} status code with content [{response.Content}]");
        }
    }
}
