using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using Shuttle.Core.Infrastructure;

namespace Shuttle.ESB.Core
{
	public class ServiceBus : IServiceBus
	{
		private static bool started;

		[ThreadStatic]
		private static string outgoingCorrelationId;

		[ThreadStatic]
		private static TransportMessage _transportMessageBeingHandled;

		[ThreadStatic]
		private static List<TransportHeader> outgoingHeaders;

		private IProcessorThreadPool controlThreadPool;
		private IProcessorThreadPool inboxThreadPool;
		private IProcessorThreadPool outboxThreadPool;
		private IProcessorThreadPool deferredMessageThreadPool;

		private readonly IEnumerable<string> EmptyPublishFlyweight =
			new ReadOnlyCollection<string>(new List<string>());

		private readonly ILog log;

		internal ServiceBus(IServiceBusConfiguration configuration)
		{
			Guard.AgainstNull(configuration, "configuration");

			log = Log.For(this);

			Configuration = configuration;

			Events = new ServiceBusEvents();
		}

		public IServiceBusConfiguration Configuration { get; private set; }
		public IServiceBusEvents Events { get; private set; }
		
		public TransportMessage TransportMessageBeingHandled {
			get { return _transportMessageBeingHandled; }
		}

		public bool IsHandlingTransportMessage {
			get { return _transportMessageBeingHandled != null; }
		}

		public void TransportMessageHandled()
		{
			_transportMessageBeingHandled = null;
		}

		public void HandlingTransportMessage(TransportMessage transportMessage)
		{
			_transportMessageBeingHandled = transportMessage;

			ResetOutgoingHeaders();

			if (transportMessage == null)
			{
				outgoingCorrelationId = string.Empty;

				return;
			}

			outgoingCorrelationId = _transportMessageBeingHandled.CorrelationId;
			outgoingHeaders.Merge(transportMessage.Headers);
		}

		public void Send(TransportMessage transportMessage)
		{
			Guard.AgainstNull(transportMessage, "transportMessage");

			var messagePipeline = Configuration.PipelineFactory.GetPipeline<SendTransportMessagePipeline>(this);

			messagePipeline.State.SetTransportMessage(transportMessage);

			if (log.IsTraceEnabled)
			{
				log.Trace(string.Format(ESBResources.TraceSend, transportMessage.MessageType,
										transportMessage.RecipientInboxWorkQueueUri));
			}

			try
			{
				messagePipeline.Execute();
			}
			finally
			{
				Configuration.PipelineFactory.ReleasePipeline(messagePipeline);
			}
		}

		public TransportMessage Send(object message)
		{
			return SendDeferred(DateTime.MinValue, message, (IQueue)null);
		}

		public TransportMessage Send(object message, string uri)
		{
			Guard.AgainstNullOrEmptyString(uri, "uri");

			return SendDeferred(DateTime.MinValue, message, Configuration.QueueManager.GetQueue(uri));
		}

		public TransportMessage Send(object message, IQueue queue)
		{
			return SendDeferred(DateTime.MinValue, message, queue);
		}

		public TransportMessage SendLocal(object message)
		{
			return SendDeferred(DateTime.MinValue, message, Configuration.Inbox.WorkQueue);
		}

		public TransportMessage SendReply(object message)
		{
			return SendDeferred(DateTime.MinValue, message, Configuration.QueueManager.GetQueue(_transportMessageBeingHandled.SenderInboxWorkQueueUri));
		}

		public TransportMessage SendDeferred(DateTime at, object message)
		{
			return SendDeferred(at, message, (IQueue)null);
		}

		public TransportMessage SendDeferred(DateTime at, object message, string uri)
		{
			Guard.AgainstNullOrEmptyString(uri, "uri");

			return SendDeferred(at, message, Configuration.QueueManager.GetQueue(uri));
		}

		public TransportMessage SendDeferred(DateTime at, object message, IQueue queue)
		{
			Guard.AgainstNull(message, "message");

			var messagePipeline = Configuration.PipelineFactory.GetPipeline<SendMessagePipeline>(this);

			if (log.IsTraceEnabled)
			{
				log.Trace(string.Format(ESBResources.TraceSend, message.GetType().FullName,
										queue == null ? "[null]" : queue.Uri.ToString()));
			}

			try
			{
				((SendMessagePipeline)messagePipeline).Execute(at, message, queue);

				return messagePipeline.State.Get<TransportMessage>(StateKeys.TransportMessage);
			}
			finally
			{
				Configuration.PipelineFactory.ReleasePipeline(messagePipeline);
			}
		}

		public TransportMessage SendDeferredLocal(DateTime at, object message)
		{
			Guard.AgainstNull(message, "message");

			if (!Configuration.HasInbox)
			{
				throw new InvalidOperationException(ESBResources.RequeueWithNoInbox);
			}

			return SendDeferred(at, message, Configuration.Inbox.WorkQueue);
		}

		public TransportMessage SendDeferredReply(DateTime at, object message)
		{
			Guard.AgainstNull(message, "message");

			if (_transportMessageBeingHandled == null)
			{
				throw new InvalidOperationException(ESBResources.ReplyWithoutCurrentMessage);
			}

			if (!_transportMessageBeingHandled.HasSenderInboxWorkQueueUri())
			{
				throw new InvalidOperationException(ESBResources.ReplyWithoutSenderInboxWorkQueueUri);
			}

			OutgoingCorrelationId = _transportMessageBeingHandled.CorrelationId;
			OutgoingHeaders.Merge(_transportMessageBeingHandled.Headers);

			return SendDeferred(at, message, Configuration.QueueManager.GetQueue(_transportMessageBeingHandled.SenderInboxWorkQueueUri));
		}

		public IEnumerable<string> Publish(object message)
		{
			Guard.AgainstNull(message, "message");

			if (Configuration.HasSubscriptionManager)
			{
				var subscribers = Configuration.SubscriptionManager.GetSubscribedUris(message).ToList();

				if (subscribers.Count > 0)
				{
					var result = new List<string>();

					foreach (var subscriber in subscribers)
					{
						Send(message, subscriber);

						result.Add(subscriber);
					}

					return result;
				}

				log.Warning(string.Format(ESBResources.WarningPublishWithoutSubscribers, message.GetType().FullName));
			}
			else
			{
				log.Warning(string.Format(ESBResources.WarningPublishWithoutSubscriptionManager, message.GetType().FullName));
			}

			return EmptyPublishFlyweight;
		}

		public IServiceBus Start()
		{
			if (Started)
			{
				throw new ApplicationException(ESBResources.ServiceBusInstanceAlreadyStarted);
			}

			GuardAgainstInvalidConfiguration();

			foreach (var module in Configuration.Modules)
			{
				module.Initialize(this);
			}

			var startupPipeline = new StartupPipeline(this);

			Events.OnPipelineCreated(this, new PipelineEventArgs(startupPipeline));

			startupPipeline.Execute();

			inboxThreadPool = startupPipeline.State.Get<IProcessorThreadPool>("InboxThreadPool");
			controlThreadPool = startupPipeline.State.Get<IProcessorThreadPool>("ControlInboxThreadPool");
			outboxThreadPool = startupPipeline.State.Get<IProcessorThreadPool>("OutboxThreadPool");
			deferredMessageThreadPool = startupPipeline.State.Get<IProcessorThreadPool>("DeferredMessageThreadPool");

			started = true;

			return this;
		}

		private void GuardAgainstInvalidConfiguration()
		{
			Guard.AgainstNullDependency(Configuration.Serializer, "serializer");
			Guard.AgainstNullDependency(Configuration.MessageHandlerFactory, "MessageHandlerFactory");

			if (Configuration.IsWorker && !Configuration.HasInbox)
			{
				throw new WorkerException(ESBResources.WorkerRequiresInbox);
			}

			if (Configuration.HasInbox)
			{
				Guard.Against<QueueConfigurationException>(Configuration.Inbox.WorkQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "Inbox.WorkQueue"));

				Guard.Against<QueueConfigurationException>(Configuration.Inbox.ErrorQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "Inbox.ErrorQueue"));
			}

			if (Configuration.HasOutbox)
			{
				Guard.Against<QueueConfigurationException>(Configuration.Outbox.WorkQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "Outbox.WorkQueue"));

				Guard.Against<QueueConfigurationException>(Configuration.Outbox.ErrorQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "Outbox.ErrorQueue"));
			}

			if (Configuration.HasControlInbox)
			{
				Guard.Against<QueueConfigurationException>(Configuration.ControlInbox.WorkQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "ControlInbox.WorkQueue"));

				Guard.Against<QueueConfigurationException>(Configuration.ControlInbox.ErrorQueue == null,
														   string.Format(ESBResources.RequiredQueueMissing,
																		 "ControlInbox.ErrorQueue"));
			}
		}

		public void Stop()
		{
			if (!Started)
			{
				return;
			}

			Configuration.Modules.AttemptDispose();

			if (Configuration.HasInbox)
			{
				inboxThreadPool.Dispose();
			}

			if (Configuration.HasControlInbox)
			{
				controlThreadPool.Dispose();
			}

			if (Configuration.HasOutbox)
			{
				outboxThreadPool.Dispose();
			}

			if (Configuration.HasDeferredQueue)
			{
				deferredMessageThreadPool.Dispose();
			}

			Configuration.QueueManager.AttemptDispose();

			started = false;
		}

		public bool Started
		{
			get { return started; }
		}

		public void Dispose()
		{
			Stop();
		}

		public TransportMessage CreateTransportMessage(object message)
		{
			Guard.AgainstNull(message, "message");

			var result = new TransportMessage();

			var identity = WindowsIdentity.GetCurrent();

			result.SenderInboxWorkQueueUri =
				Configuration.HasInbox
					? Configuration.Inbox.WorkQueue.Uri.ToString()
					: string.Empty;

			result.PrincipalIdentityName = identity != null
											   ? identity.Name
											   : WindowsIdentity.GetAnonymous().Name;

			if (result.SendDate == DateTime.MinValue)
			{
				result.SendDate = DateTime.Now;
			}

			result.Message = Configuration.Serializer.Serialize(message).ToBytes();
			result.MessageType = message.GetType().FullName;
			result.AssemblyQualifiedName = message.GetType().AssemblyQualifiedName;

			result.EncryptionAlgorithm = Configuration.EncryptionAlgorithm;
			result.CompressionAlgorithm = Configuration.CompressionAlgorithm;

			if (_transportMessageBeingHandled != null)
			{
				result.MessageReceivedId = _transportMessageBeingHandled.MessageId;
			}

			return result;
		}

		public static IServiceBusConfigurationBuilder Create()
		{
			return new ServiceBusConfigurationBuilder();
		}

		public static IServiceBus StartEndpoint()
		{
			var starters = new ReflectionService().GetTypes<IStartEndpoint>().ToList();

			if (starters.Count != 1)
			{
				throw new ApplicationException(string.Format(ESBResources.StartEndpointException, starters.Count()));
			}

			var type = starters.ElementAt(0);

			type.AssertDefaultConstructor(string.Format(ESBResources.StartEndpointRequiresDefaultConstructor, type.FullName));

			return ((IStartEndpoint)Activator.CreateInstance(type)).Start();
		}

		public string OutgoingCorrelationId
		{
			get { return outgoingCorrelationId; }
			set { outgoingCorrelationId = value; }
		}

		public List<TransportHeader> OutgoingHeaders
		{
			get { return outgoingHeaders ?? (outgoingHeaders = new List<TransportHeader>()); }
		}

		public void ResetOutgoingHeaders()
		{
			OutgoingHeaders.Clear();
		}
	}
}