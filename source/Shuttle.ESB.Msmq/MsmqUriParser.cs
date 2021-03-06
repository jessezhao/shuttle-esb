﻿using System;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Web;
using Shuttle.Core.Infrastructure;
using Shuttle.ESB.Core;

namespace Shuttle.ESB.Msmq
{
	public class MsmqUriParser
	{
		internal const string SCHEME = "msmq";

		private string host;
		private bool usesIPAddress;

		private readonly Regex regexIPAddress =
			new Regex(
				@"^([1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])(\.([0-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])){3}$");

		public MsmqUriParser(Uri uri)
		{
			Guard.AgainstNull(uri, "uri");

			if (!uri.Scheme.Equals(SCHEME, StringComparison.InvariantCultureIgnoreCase))
			{
				throw new InvalidSchemeException(SCHEME, uri.ToString());
			}

			var builder = new UriBuilder(uri);

			host = uri.Host;

			if (host.Equals("."))
			{
				builder.Host = Environment.MachineName.ToLower();
			}

			if (uri.LocalPath == "/")
			{
				throw new UriFormatException(string.Format(ESBResources.UriFormatException, "msmq://{{host-name}}/{{queue-name}}",
														   uri));
			}

			Uri = builder.Uri;

			Local = Uri.Host.Equals(Environment.MachineName, StringComparison.InvariantCultureIgnoreCase);

			usesIPAddress = regexIPAddress.IsMatch(host);

			Path = Local
					   ? string.Format(@"{0}\private$\{1}", host, uri.Segments[1])
					   : usesIPAddress
							 ? string.Format(@"FormatName:DIRECT=TCP:{0}\private$\{1}", host, uri.Segments[1])
							 : string.Format(@"FormatName:DIRECT=OS:{0}\private$\{1}", host, uri.Segments[1]);

			JournalPath = string.Concat(Path, "$journal");

			var parameters = HttpUtility.ParseQueryString(uri.Query);

			SetJournal(parameters);
			SetTransactional(parameters);
		}

		private void SetTransactional(NameValueCollection parameters)
		{
			Transactional = true;

			var transactionalItem = parameters.Get("transactional");

			if (transactionalItem == null)
			{
				return;
			}

			bool transactional;

			if (bool.TryParse(transactionalItem, out transactional))
			{
				Transactional = transactional;
			}
		}

		private void SetJournal(NameValueCollection parameters)
		{
			Journal = true;

			var journalItem = parameters.Get("journal");

			if (journalItem == null)
			{
				return;
			}

			bool journal;

			if (bool.TryParse(journalItem, out journal))
			{
				Journal = journal;
			}
		}

		public Uri Uri { get; private set; }
		public bool Local { get; private set; }
		public bool Transactional { get; private set; }
		public bool Journal { get; private set; }
		public string Path { get; private set; }
		public string JournalPath { get; private set; }
	}
}