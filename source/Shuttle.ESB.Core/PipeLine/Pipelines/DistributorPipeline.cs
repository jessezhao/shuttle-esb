﻿namespace Shuttle.ESB.Core
{
	public class DistributorPipeline : MessagePipeline
	{
		public DistributorPipeline(IServiceBus bus)
			: base(bus)
		{
			RegisterStage("Distribute")
				.WithEvent<OnGetMessage>()
				.WithEvent<OnDeserializeTransportMessage>()
				.WithEvent<OnAfterDeserializeTransportMessage>()
				.WithEvent<OnHandleDistributeMessage>()
				.WithEvent<OnAfterHandleDistributeMessage>()
				.WithEvent<OnSerializeTransportMessage>()
				.WithEvent<OnAfterSerializeTransportMessage>()
				.WithEvent<OnSendMessage>()
				.WithEvent<OnAfterSendMessage>();

			RegisterObserver(new GetWorkMessageObserver());
			RegisterObserver(new DeserializeTransportMessageObserver());
			RegisterObserver(new DistributorMessageObserver());
			RegisterObserver(new SerializeTransportMessageObserver());
			RegisterObserver(new SendMessageObserver());
			RegisterObserver(new DistributorExceptionObserver());
		}

		public override sealed void Obtained()
		{
			base.Obtained();

			State.SetWorkQueue(_bus.Configuration.Inbox.WorkQueue);
			State.SetErrorQueue(_bus.Configuration.Inbox.ErrorQueue);
		}
	}
}