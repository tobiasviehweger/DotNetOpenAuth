//-----------------------------------------------------------------------
// <copyright file="ChannelContract.cs" company="Outercurve Foundation">
//     Copyright (c) Outercurve Foundation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.Messaging {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.Contracts;

	/// <summary>
	/// Code contract for the <see cref="Channel"/> class.
	/// </summary>
	[ContractClassFor(typeof(Channel))]
	internal abstract class ChannelContract : Channel {
		/// <summary>
		/// Prevents a default instance of the ChannelContract class from being created.
		/// </summary>
		private ChannelContract()
			: base(null, null) {
		}

		/// <summary>
		/// Gets the protocol message that may be in the given HTTP response.
		/// </summary>
		/// <param name="response">The response that is anticipated to contain an protocol message.</param>
		/// <returns>
		/// The deserialized message parts, if found.  Null otherwise.
		/// </returns>
		/// <exception cref="ProtocolException">Thrown when the response is not valid.</exception>
		protected override IDictionary<string, string> ReadFromResponseCore(IncomingWebResponse response) {
			Requires.NotNull(response, "response");
			throw new NotImplementedException();
		}
	}
}
