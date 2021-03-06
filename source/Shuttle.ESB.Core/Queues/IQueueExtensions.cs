using System;
using Shuttle.Core.Infrastructure;

namespace Shuttle.ESB.Core
{
	public static class IQueueExtensions
	{
		public static bool AttemptCreate(this IQueue queue)
		{
			var operation = queue as ICreate;

			if (operation == null)
			{
				return false;
			}

			operation.Create();

			return true;
		}
		
		public static void Create(this IQueue queue)
		{
			Guard.AgainstNull(queue, "queue");

			var operation = queue as ICreate;

			if (operation == null)
			{
				throw new InvalidOperationException(string.Format(ESBResources.NotImplementedOnQueue,
																  queue.GetType().FullName, "ICreate"));
			}

			operation.Create();
		}
		
		public static bool AttemptDrop(this IQueue queue)
		{
			var operation = queue as IDrop;

			if (operation == null)
			{
				return false;
			}

			operation.Drop();

			return true;
		}
		
		public static void Drop(this IQueue queue)
		{
			Guard.AgainstNull(queue, "queue");

			var operation = queue as IDrop;

			if (operation == null)
			{
				throw new InvalidOperationException(string.Format(ESBResources.NotImplementedOnQueue,
																  queue.GetType().FullName, "IDrop"));
			}

			operation.Drop();
		}
		
		public static bool AttemptPurge(this IQueue queue)
		{
			var operation = queue as IPurge;

			if (operation == null)
			{
				return false;
			}

			operation.Purge();

			return true;
		}
		
		public static void Purge(this IQueue queue)
		{
			Guard.AgainstNull(queue, "queue");

			var operation = queue as IPurge;

			if (operation == null)
			{
				throw new InvalidOperationException(string.Format(ESBResources.NotImplementedOnQueue,
																  queue.GetType().FullName, "IPurge"));
			}

			operation.Purge();
		}
	}
}