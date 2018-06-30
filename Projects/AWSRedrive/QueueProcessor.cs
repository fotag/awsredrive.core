﻿using System;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using NLog;

namespace AWSRedrive
{
    public class QueueProcessor : IQueueProcessor
    {
        public ConfigurationEntry Configuration { get; set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IQueueClient _queueClient;
        private IMessageProcessor _messageProcessor;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private int _messagesReceived;
        private int _messagesSent;
        private int _messagesFailed;

        public void Init(IQueueClient queueClient, 
            IMessageProcessor messageProcessor, 
            ConfigurationEntry configuration)
        {
            Configuration = configuration;
            _queueClient = queueClient;
            _messageProcessor = messageProcessor;
        }

        public void Start()
        {
            if (_task != null)
            {
                Logger.Info($"Queue processor [{Configuration.Alias}] is already started");
                return;
            }

            _cancellation = new CancellationTokenSource();
            _task = new Task(ProcessMessageLoop, _cancellation.Token, TaskCreationOptions.LongRunning);
            _task.Start();
        }

        public void Stop()
        {
            if (_task == null)
            {
                Logger.Info($"Queue processor [{Configuration.Alias}] is already stopped");
                return;
            }

            try
            {
                _cancellation.Cancel();
                _cancellation.Token.WaitHandle.WaitOne(30 * 1000);
                _cancellation.Dispose();
                _task.Dispose();
            }
            catch (Exception e)
            {
                Logger.Warn($"Queue processor [{Configuration.Alias}] has not stopped gracefully - {e}");
            }
            finally
            {
                _task = null;
            }
        }

        public void ProcessMessageLoop()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                Logger.Debug($"Waiting for message, queue processor [{Configuration.Alias}]");
                var msg = _queueClient.GetMessage();
                if (msg == null)
                {
                    Logger.Debug($"No message received, queue processor [{Configuration.Alias}]");
                    continue;
                }

                _messagesReceived++;

                Logger.Debug($"Message received, queue processor [{Configuration.Alias}]");
                if (Logger.IsTraceEnabled)
                {
                    Logger.Trace($"[{Configuration.Alias}]: {msg.Content}");
                }

                try
                {
                    Logger.Debug($"Processing message, queue processor [{Configuration.Alias}], url {Configuration.RedriveUrl}");
                    _messageProcessor.ProcessMessage(msg.Content, Configuration);
                    Logger.Debug($"Processing complete, queue processor [{Configuration.Alias}]");

                    _messagesSent++;
                }
                catch (Exception e)
                {
                    _messagesFailed++;

                    Logger.Error($"Error processing message [{msg.MessageIdentifier}, queue processor [{Configuration.Alias}] - {e}");
                    Logger.Error($"Message [{msg.MessageIdentifier}, queue processor [{Configuration.Alias}] follows \r\n{msg.Content}");
                }
                finally
                {
                    Logger.Info($"Queue processor [{Configuration.Alias}], messages received {_messagesReceived}, sent {_messagesSent}, failed {_messagesFailed}");
                }

                try
                {
                    Logger.Debug($"Deleting message, queue processor [{Configuration.Alias}, id [{msg.MessageIdentifier}");
                    _queueClient.DeleteMessage(msg);
                    Logger.Debug($"Message deleted, queue processor [{Configuration.Alias}, id [{msg.MessageIdentifier}");
                }
                catch (Exception e)
                {
                    Logger.Error($"Could not delete message [{msg.MessageIdentifier}, queue processor [{Configuration.Alias}] - MESSAGE REMAINS IN QUEUE! - {e}");
                }
            }
        }
    }
}