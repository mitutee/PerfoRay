﻿using Microsoft.AspNetCore.Http;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using BLL;
using System.Linq;
using System.Collections.Generic;

namespace PerfoRay
{
    /// <summary>
    /// Handles incoming ws:// connections
    /// Main controller
    /// </summary>
    public class WebSocketHandler
    {
        private WebSocket _socket { get; }

        private WebSocketHandler(WebSocket socket)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            _socket = socket;
        }

        public static ScansManager Manager { get; set; }

        /// <summary>
        /// Start point for <see cref="WebSocketHandler"/> pipeline
        /// </summary>
        /// <param name="httpContext"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public static async Task Acceptor(HttpContext httpContext, Func<Task> next)
        {
            if (!httpContext.WebSockets.IsWebSocketRequest) return;

            using (var socket = await httpContext.WebSockets.AcceptWebSocketAsync())
            {
                var handler = new WebSocketHandler(socket);
                await handler.processAsync(httpContext.RequestAborted);
            }
            
        }

        private async Task processAsync(CancellationToken ct = default(CancellationToken))
        {
            StartScaningArgs args = await receiveJsonAsync<StartScaningArgs>(ct);

            Scanner sk = new Scanner(args.Uri);
            sk.MeasuringStarted += sendMeasureStartedMsg;
            sk.MeasuringEnded += sendMeasureEndedMsg;
            ScanResult result = sk.Scan();

            result.Pages.OrderByDescending(el => el.DownloadTime);
            Manager.Create(result);

            await sendJsonAsync(result);
        }

        private void sendMeasureEndedMsg(object sender, DocumentResult e)
        {
            sendJsonAsync(new
            {
                Type = "measure_ended",
                Result = e
            }).Wait();
        }

        private void sendMeasureStartedMsg(object sender, Uri e)
        {
             sendJsonAsync(new
            {
                Type = "measure_started",
                Uri = e.AbsoluteUri,
            }).Wait();
        }

        private async Task<T> receiveJsonAsync<T>(CancellationToken ct = default(CancellationToken))
        {
            string json = await receiveStringAsync(ct);
            return JsonConvert.DeserializeObject<T>(json, createJsonSettings());
        }

        private async Task<string> receiveStringAsync(CancellationToken ct = default(CancellationToken))
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    ct.ThrowIfCancellationRequested();

                    result = await _socket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new Exception("Unexpected message");

                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        private JsonSerializerSettings createJsonSettings()
        {
            return new JsonSerializerSettings()
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        }

        private async Task sendJsonAsync(object data, CancellationToken ct = default(CancellationToken))
        {
            string json = JsonConvert.SerializeObject(data, createJsonSettings());
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            try
            {
                await _socket.SendAsync(segment, WebSocketMessageType.Text, true, ct).ConfigureAwait(true);
            }
            catch (Exception)
            {

                throw;
            }

        }
    }
}