﻿namespace Shuttle.ESB.Core
{
	public abstract class ReceiveMessagePipeline : MessagePipeline
	{
		protected ReceiveMessagePipeline(IServiceBus bus)
			: base(bus)
		{
			RegisterStage("Read")
				.WithEvent<OnStartTransactionScope>()
				.WithEvent<OnDequeue>()
				.WithEvent<OnDeserializeTransportMessage>()
				.WithEvent<OnDecompressMessage>()
				.WithEvent<OnDecryptMessage>()
				.WithEvent<OnDeserializeMessage>();

			RegisterStage("Handle")
				.WithEvent<OnMessageReceived>()
				.WithEvent<OnHandleMessage>()
				.WithEvent<OnMessageHandled>()
				.WithEvent<OnCompleteTransactionScope>()
				.WithEvent<OnDisposeTransactionScope>()
				.WithEvent<OnAcknowledgeMessage>();

			RegisterObserver(new DequeueObserver());
			RegisterObserver(new DeserializeTransportMessageObserver());
			RegisterObserver(new DeserializeMessageObserver());
			RegisterObserver(new DecryptMessageObserver());
			RegisterObserver(new DecompressMessageObserver());
			RegisterObserver(new HandleMessageObserver());
			RegisterObserver(new ReceiveExceptionObserver());
			RegisterObserver(new TransactionScopeObserver());
			RegisterObserver(new AcknowledgeMessageObserver());
			RegisterObserver(new ReceiveMessageStateObserver());
		}
	}
}