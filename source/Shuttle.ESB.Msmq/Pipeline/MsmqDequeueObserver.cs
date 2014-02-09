using System;
using System.Diagnostics;
using System.Messaging;
using System.Security.Principal;
using Shuttle.Core.Infrastructure;

namespace Shuttle.ESB.Msmq
{
	public class MsmqDequeueObserver :
		IPipelineObserver<OnStart>,
		IPipelineObserver<OnReceiveMessage>,
		IPipelineObserver<OnSendJournalMessage>,
		IPipelineObserver<OnDispose>
	{
		private readonly MessagePropertyFilter _messagePropertyFilter;
		private readonly ILog _log;

		public MsmqDequeueObserver()
		{
			_messagePropertyFilter = new MessagePropertyFilter();
			_messagePropertyFilter.SetAll();

			_log = Log.For(this);
		}

		public void Execute(OnStart pipelineEvent)
		{
			var parser = pipelineEvent.Pipeline.State.Get<MsmqUriParser>();

			pipelineEvent.Pipeline.State.Add("queue", new MessageQueue(parser.Path)
				{
					MessageReadPropertyFilter = _messagePropertyFilter
				});

			if (parser.Journal)
			{
				pipelineEvent.Pipeline.State.Add("journalQueue", new MessageQueue(parser.JournalPath)
					{
						MessageReadPropertyFilter = _messagePropertyFilter
					});
			}
		}

		public void Execute(OnDispose pipelineEvent)
		{
			var queue = pipelineEvent.Pipeline.State.Get<MessageQueue>("queue");

			if (queue != null)
			{
				queue.Dispose();
			}

			var journalQueue = pipelineEvent.Pipeline.State.Get<MessageQueue>("journalQueue");

			if (journalQueue != null)
			{
				journalQueue.Dispose();
			}
		}

		public void Execute(OnReceiveMessage pipelineEvent)
		{
			var parser = pipelineEvent.Pipeline.State.Get<MsmqUriParser>();
			var tx = pipelineEvent.Pipeline.State.Get<MessageQueueTransaction>();

			try
			{
				var message = tx != null
					                  ? pipelineEvent.Pipeline.State.Get<MessageQueue>("queue")
					                                 .Receive(pipelineEvent.Pipeline.State.Get<TimeSpan>("timeout"), tx)
					                  : pipelineEvent.Pipeline.State.Get<MessageQueue>("queue")
					                                 .Receive(pipelineEvent.Pipeline.State.Get<TimeSpan>("timeout"),
					                                          MessageQueueTransactionType.None);

				pipelineEvent.Pipeline.State.Add(message);
			}
			catch (MessageQueueException ex)
			{
				if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
				{
					pipelineEvent.Pipeline.State.Add<Message>(null);
					return;
				}

				if (ex.MessageQueueErrorCode == MessageQueueErrorCode.AccessDenied)
				{
					MsmqQueue.AccessDenied(_log, parser.Path);
				}

				_log.Error(string.Format(MsmqResources.DequeueError, parser.Uri, ex.Message));

				throw;
			}
		}

		public void Execute(OnSendJournalMessage pipelineEvent)
		{
			var journalQueue = pipelineEvent.Pipeline.State.Get<MessageQueue>("journalQueue");
			var message = pipelineEvent.Pipeline.State.Get<Message>();

			if (journalQueue == null || message == null)
			{
				return;
			}

			var journalMessage = new Message
				{
					Recoverable = true,
					Label = message.Label,
					CorrelationId = string.Format(@"{0}\1", message.Label),
					BodyStream = message.BodyStream.Copy()
				};

			var tx = pipelineEvent.Pipeline.State.Get<MessageQueueTransaction>();

			if (tx != null)
			{
				journalQueue.Send(journalMessage, tx);
			}
			else
			{
				journalQueue.Send(journalMessage, MessageQueueTransactionType.None);
			}
		}
	}
}