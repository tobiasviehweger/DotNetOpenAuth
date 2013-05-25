﻿//-----------------------------------------------------------------------
// <copyright file="OutgoingWebResponse.cs" company="Outercurve Foundation">
//     Copyright (c) Outercurve Foundation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.Messaging {
	using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Threading;

	/// <summary>
	/// A protocol message (request or response) that passes from this
	/// to a remote party via the user agent using a redirect or form 
	/// POST submission, OR a direct message response.
	/// </summary>
	/// <remarks>
	/// <para>An instance of this type describes the HTTP response that must be sent
	/// in response to the current HTTP request.</para>
	/// <para>It is important that this response make up the entire HTTP response.
	/// A hosting ASPX page should not be allowed to render its normal HTML output
	/// after this response is sent.  The normal rendered output of an ASPX page 
	/// can be canceled by calling <see cref="HttpResponse.End"/> after this message
	/// is sent on the response stream.</para>
	/// </remarks>
	public class OutgoingWebResponse {
		/// <summary>
		/// The encoder to use for serializing the response body.
		/// </summary>
		private static Encoding bodyStringEncoder = new UTF8Encoding(false);

		/// <summary>
		/// Initializes a new instance of the <see cref="OutgoingWebResponse"/> class.
		/// </summary>
		internal OutgoingWebResponse() {
			this.Status = HttpStatusCode.OK;
			this.Headers = new WebHeaderCollection();
			this.Cookies = new List<Cookie>();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="OutgoingWebResponse"/> class
		/// based on the contents of an <see cref="HttpWebResponse"/>.
		/// </summary>
		/// <param name="response">The <see cref="HttpWebResponse"/> to clone.</param>
		/// <param name="maximumBytesToRead">The maximum bytes to read from the response stream.</param>
		protected internal OutgoingWebResponse(HttpWebResponse response, int maximumBytesToRead) {
			Requires.NotNull(response, "response");

			this.Status = response.StatusCode;
			this.Headers = response.Headers;
			this.Cookies = new List<Cookie>();
			this.ResponseStream = new MemoryStream(response.ContentLength < 0 ? 4 * 1024 : (int)response.ContentLength);
			using (Stream responseStream = response.GetResponseStream()) {
				// BUGBUG: strictly speaking, is the response were exactly the limit, we'd report it as truncated here.
				this.IsResponseTruncated = responseStream.CopyUpTo(this.ResponseStream, maximumBytesToRead) == maximumBytesToRead;
				this.ResponseStream.Seek(0, SeekOrigin.Begin);
			}
		}

		/// <summary>
		/// Gets the headers that must be included in the response to the user agent.
		/// </summary>
		/// <remarks>
		/// The headers in this collection are not meant to be a comprehensive list
		/// of exactly what should be sent, but are meant to augment whatever headers
		/// are generally included in a typical response.
		/// </remarks>
		public WebHeaderCollection Headers { get; internal set; }

		/// <summary>
		/// Gets the body of the HTTP response.
		/// </summary>
		public Stream ResponseStream { get; internal set; }

		/// <summary>
		/// Gets a value indicating whether the response stream is incomplete due
		/// to a length limitation imposed by the HttpWebRequest or calling method.
		/// </summary>
		public bool IsResponseTruncated { get; internal set; }

		/// <summary>
		/// Gets the cookies collection to add as headers to the HTTP response.
		/// </summary>
		public List<Cookie> Cookies { get; internal set; }

		/// <summary>
		/// Gets or sets the body of the response as a string.
		/// </summary>
		public string Body {
			get { return this.ResponseStream != null ? this.GetResponseReader().ReadToEnd() : null; }
			set { this.SetResponse(value, null); }
		}

		/// <summary>
		/// Gets the HTTP status code to use in the HTTP response.
		/// </summary>
		public HttpStatusCode Status { get; internal set; }

		/// <summary>
		/// Gets or sets a reference to the actual protocol message that
		/// is being sent via the user agent.
		/// </summary>
		internal IProtocolMessage OriginalMessage { get; set; }

		/// <summary>
		/// Creates a text reader for the response stream.
		/// </summary>
		/// <returns>The text reader, initialized for the proper encoding.</returns>
		[SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Costly operation")]
		public StreamReader GetResponseReader() {
			this.ResponseStream.Seek(0, SeekOrigin.Begin);
			string contentEncoding = this.Headers[HttpResponseHeader.ContentEncoding];
			if (string.IsNullOrEmpty(contentEncoding)) {
				return new StreamReader(this.ResponseStream);
			} else {
				return new StreamReader(this.ResponseStream, Encoding.GetEncoding(contentEncoding));
			}
		}

		/// <summary>
		/// Automatically sends the appropriate response to the user agent.
		/// </summary>
		/// <param name="response">The response to set to this message.</param>
		public virtual void Send(HttpListenerResponse response) {
			Requires.NotNull(response, "response");

			response.StatusCode = (int)this.Status;
			MessagingUtilities.ApplyHeadersToResponse(this.Headers, response);
			foreach (Cookie httpCookie in this.Cookies) {
				var cookie = new Cookie(httpCookie.Name, httpCookie.Value) {
					Expires = httpCookie.Expires,
					Path = httpCookie.Path,
					HttpOnly = httpCookie.HttpOnly,
					Secure = httpCookie.Secure,
					Domain = httpCookie.Domain,
				};
				response.AppendCookie(cookie);
			}

			if (this.ResponseStream != null) {
				response.ContentLength64 = this.ResponseStream.Length;
				this.ResponseStream.CopyTo(response.OutputStream);
			}

			response.OutputStream.Close();
		}

		/// <summary>
		/// Gets the URI that, when requested with an HTTP GET request,
		/// would transmit the message that normally would be transmitted via a user agent redirect.
		/// </summary>
		/// <param name="channel">The channel to use for encoding.</param>
		/// <returns>
		/// The URL that would transmit the original message.  This URL may exceed the normal 2K limit,
		/// and should therefore be broken up manually and POSTed as form fields when it exceeds this length.
		/// </returns>
		/// <remarks>
		/// This is useful for desktop applications that will spawn a user agent to transmit the message
		/// rather than cause a redirect.
		/// </remarks>
		internal Uri GetDirectUriRequest(Channel channel) {
			Requires.NotNull(channel, "channel");

			var message = this.OriginalMessage as IDirectedProtocolMessage;
			if (message == null) {
				throw new InvalidOperationException(); // this only makes sense for directed messages (indirect responses)
			}

			var fields = channel.MessageDescriptions.GetAccessor(message).Serialize();
			UriBuilder builder = new UriBuilder(message.Recipient);
			MessagingUtilities.AppendQueryArgs(builder, fields);
			return builder.Uri;
		}

		/// <summary>
		/// Sets the response to some string, encoded as UTF-8.
		/// </summary>
		/// <param name="body">The string to set the response to.</param>
		/// <param name="contentType">Type of the content.  May be null.</param>
		internal void SetResponse(string body, ContentType contentType) {
			if (body == null) {
				this.ResponseStream = null;
				return;
			}

			if (contentType == null) {
				contentType = new ContentType("text/html");
				contentType.CharSet = bodyStringEncoder.WebName;
			} else if (contentType.CharSet != bodyStringEncoder.WebName) {
				// clone the original so we're not tampering with our inputs if it came as a parameter.
				contentType = new ContentType(contentType.ToString());
				contentType.CharSet = bodyStringEncoder.WebName;
			}

			this.Headers[HttpResponseHeader.ContentType] = contentType.ToString();
			this.ResponseStream = new MemoryStream();
			StreamWriter writer = new StreamWriter(this.ResponseStream, bodyStringEncoder);
			writer.Write(body);
			writer.Flush();
			this.ResponseStream.Seek(0, SeekOrigin.Begin);
			this.Headers[HttpResponseHeader.ContentLength] = this.ResponseStream.Length.ToString(CultureInfo.InvariantCulture);
		}
	}
}
