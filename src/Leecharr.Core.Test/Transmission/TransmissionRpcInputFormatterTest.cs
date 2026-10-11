// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.Transmission;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;

namespace Leecharr.Core.Test.Transmission;

[TestFixture]
public class TransmissionRpcInputFormatterTest
{
    private TransmissionRpcInputFormatter _formatter;

    [SetUp]
    public void SetUp()
    {
        _formatter = new TransmissionRpcInputFormatter();
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_deserialize_valid_json_from_non_seekable_stream()
    {
        var json = "{\"method\": \"torrent-start\", \"arguments\": { \"ids\": [1, 2] }, \"tag\": 456}";
        var innerStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var nonSeekableStream = new NonSeekableStream(innerStream);
        var context = CreateContext(nonSeekableStream, contentType: "application/json");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("torrent-start"));
        Assert.That(request.Arguments.ContainsKey("ids"), Is.True);
        Assert.That(request.Tag.GetInt32(), Is.EqualTo(456));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_preserve_plus_in_form_urlencoded_json_on_non_seekable_stream()
    {
        var json = "{\"method\":\"torrent-add\",\"arguments\":{\"metainfo\":\"YWJj+ZGVm\"}}";
        var innerStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var nonSeekableStream = new NonSeekableStream(innerStream);
        var context = CreateContext(nonSeekableStream, contentType: "application/x-www-form-urlencoded");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("torrent-add"));
        Assert.That(request.Arguments["metainfo"].GetString(), Is.EqualTo("YWJj+ZGVm"));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_extract_json_from_form_url_encoded_content()
    {
        var formFields = new Dictionary<string, StringValues>
        {
            { "{\"method\":\"session-get\"}", string.Empty },
        };
        var formCollection = new FormCollection(formFields);
        var stream = new NonSeekableStream(new MemoryStream());
        var context = CreateContext(stream, contentType: "application/x-www-form-urlencoded", form: formCollection);

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("session-get"));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_deserialize_form_with_query_field()
    {
        var json = "{\"method\":\"session-get\",\"tag\":123}";
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "query", json },
        });

        var context = CreateContext(
            new MemoryStream(Encoding.UTF8.GetBytes($"query={Uri.EscapeDataString(json)}")),
            contentType: "application/x-www-form-urlencoded",
            form: form);

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var req = result.Model as TransmissionRpcRequest;
        Assert.That(req, Is.Not.Null);
        Assert.That(req.Method, Is.EqualTo("session-get"));
        Assert.That(req.Tag.GetInt32(), Is.EqualTo(123));
    }

    private static InputFormatterContext CreateContext(
        Stream bodyStream,
        string contentType = "application/json",
        Type modelType = null,
        IFormCollection form = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = bodyStream;

        if (contentType != null)
        {
            httpContext.Request.ContentType = contentType;
        }

        if (form != null)
        {
            httpContext.Request.Form = form;
        }

        var modelState = new ModelStateDictionary();
        var metadataProvider = new EmptyModelMetadataProvider();
        var metadata = metadataProvider.GetMetadataForType(modelType ?? typeof(TransmissionRpcRequest));

        return new InputFormatterContext(
            httpContext,
            modelName: "request",
            modelState: modelState,
            metadata: metadata,
            readerFactory: (stream, encoding) => new StreamReader(stream, encoding));
    }

    private class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
