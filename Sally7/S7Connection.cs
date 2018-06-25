﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sally7.Infrastructure;
using Sally7.Protocol;
using Sally7.Protocol.Cotp;
using Sally7.Protocol.Cotp.Messages;
using Sally7.Protocol.IsoOverTcp;
using Sally7.Protocol.S7;
using Sally7.Protocol.S7.Messages;

namespace Sally7
{
    public class S7Connection
    {
        private const int IsoOverTcpPort = 102;

        private readonly string host;
        private readonly Tsap sourceTsap;
        private readonly Tsap destinationTsap;
        private readonly TcpClient client = new TcpClient {NoDelay = true};

        private byte[] buffer = new byte[100];
        private int pduSize;

        public S7Connection(string host, Tsap sourceTsap, Tsap destinationTsap)
        {
            this.host = host;
            this.sourceTsap = sourceTsap;
            this.destinationTsap = destinationTsap;
        }

        public void Close()
        {
            client.Close();
        }

        public async Task Open()
        {
            await client.ConnectAsync(host, IsoOverTcpPort).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(buffer, 0, BuildConnectRequest()).ConfigureAwait(false);
            ParseConnectionConfirm(await ReadTpkt().ConfigureAwait(false));

            await stream.WriteAsync(buffer, 0, BuildCommunicationSetup()).ConfigureAwait(false);
            ParseCommunicationSetup(await ReadTpkt().ConfigureAwait(false));
        }

        public async Task Read(params IDataItem[] dataItems)
        {
            var stream = client.GetStream();
            await stream.WriteAsync(buffer, 0, BuildReadRequest(dataItems)).ConfigureAwait(false);
            ParseReadResponse(dataItems, await ReadTpkt().ConfigureAwait(false));
        }

        public async Task Write(params IDataItem[] dataItems)
        {
            var stream = client.GetStream();
            await stream.WriteAsync(buffer, 0, BuildWriteRequest(dataItems)).ConfigureAwait(false);
            ParseWriteResponse(dataItems, await ReadTpkt().ConfigureAwait(false));
        }

        private async Task<int> ReadTpkt()
        {
            var stream = client.GetStream();

            var len = await stream.ReadAsync(buffer, 0, 4).ConfigureAwait(false);
            if (len < 4)
                throw new Exception($"Error while reading TPKT header, expected 4 bytes but received {len}.");

            buffer.Struct<Tpkt>(0).Assert();
            var msgLen = buffer.Struct<Tpkt>(0).MessageLength();
            len = await stream.ReadAsync(buffer, 4, msgLen);
            if (len != msgLen)
            {
                throw new Exception($"Error while reading TPKT data, expected {msgLen} bytes but received {len}.");
            }

            return buffer.Struct<Tpkt>(0).Length;
        }

        private int BuildConnectRequest()
        {
            ref var message = ref buffer.Struct<ConnectionRequestMessage>(4);
            message.Init(PduSizeParameter.PduSize.Pdu512, sourceTsap, destinationTsap);
            var len = 4 + ConnectionRequestMessage.Size;
            buffer.Struct<Tpkt>(0).Init(len);

            DumpBuffer(len);

            return len;
        }

        private int BuildCommunicationSetup()
        {
            ref var data = ref buffer.Struct<Data>(4);
            data.Init();

            ref var header = ref buffer.Struct<Header>(7);
            header.Init(MessageType.JobRequest, CommunicationSetup.Size, 0);

            // Error class and error code are not used, so next starts at 7 + 10
            ref var setup = ref buffer.Struct<CommunicationSetup>(17);
            setup.Init(1, 1, 1920);

            var len = 17 + CommunicationSetup.Size;
            buffer.Struct<Tpkt>(0).Init(len);

            DumpBuffer(len);

            return len;
        }

        private int BuildReadRequest(in IReadOnlyList<IDataItem> dataItems)
        {
            ref var read = ref buffer.Struct<ReadRequest>(17);
            read.FunctionCode = FunctionCode.Read;
            read.ItemCount = (byte) dataItems.Count;
            var parameters = MemoryMarshal.Cast<byte, RequestItem>(buffer.AsSpan().Slice(19));
            for (var i = 0; i < dataItems.Count; i++)
            {
                BuildRequestItem(ref parameters[i], dataItems[i]);
                parameters[i].Count = dataItems[i].ReadCount;
            }

            return BuildS7JobRequest(dataItems.Count * 12 + 2, 0);
        }

        private int BuildWriteRequest(in IReadOnlyList<IDataItem> dataItems)
        {
            var span = buffer.AsSpan().Slice(17); // Skip header
            span[0] = (byte) FunctionCode.Write;
            span[1] = (byte) dataItems.Count;
            var parameters = MemoryMarshal.Cast<byte, RequestItem>(span.Slice(2));
            var fnParameterLength = dataItems.Count * 12 + 2;
            var dataLength = 0;
            var data = span.Slice(fnParameterLength);
            for (var i = 0; i < dataItems.Count; i++)
            {
                BuildRequestItem(ref parameters[i], dataItems[i]);

                ref var dataItem = ref data.Struct<DataItem>(0);
                dataItem.ErrorCode = 0;
                dataItem.TransportSize = dataItems[i].TransportSize;

                var length = dataItems[i].WriteValue(data.Slice(4));
                parameters[i].Count = length;
                dataItem.Count = dataItem.TransportSize.IsSizeInBytes() ? length : length << 3;

                length += 4; // Add sizeof(DataItem)
                if (length % 2 == 1)
                {
                    data[++length] = 0;
                }
                dataLength += length;

                data = data.Slice(length);
            }

            return BuildS7JobRequest(fnParameterLength, dataLength);
        }

        private void BuildRequestItem(ref RequestItem requestItem, in IDataItem dataItem)
        {
            requestItem.Init();
            requestItem.Address = dataItem.Address;
            requestItem.Area = dataItem.Area;
            requestItem.DbNumber = dataItem.DbNumber;
            requestItem.VariableType = dataItem.VariableType;
        }

        private int BuildS7JobRequest(in BigEndianShort parameterLength, in BigEndianShort dataLength)
        {
            var len = parameterLength + dataLength + 17; // Error omitted
            buffer.Struct<Tpkt>(0).Init(len);
            buffer.Struct<Data>(4).Init();
            buffer.Struct<Header>(7).Init(MessageType.JobRequest, parameterLength, dataLength);

            DumpBuffer(len);

            return len;
        }

        private void ParseConnectionConfirm(in int length)
        {
            DumpBuffer(length);

            var fixedPartLength = buffer[5];
            if (fixedPartLength < ConnectionConfirm.Size)
                throw new Exception("Received data is smaller than Connection Confirm fixed part.");

            ref var cc = ref buffer.Struct<ConnectionConfirm>(5);
            cc.Assert();

            // Analyze returned parameters?
        }

        private void ParseCommunicationSetup(in int length)
        {
            DumpBuffer(length);
            if (length < 19 + CommunicationSetup.Size) throw new Exception("Received data is smaller than TPKT + DT PDU + S7 header + S7 communication setup size.");
            ref var dt = ref buffer.Struct<Data>(4);
            dt.Assert();

            ref var s7Header = ref buffer.Struct<Header>(7);
            s7Header.Assert(MessageType.AckData);

            ref var s7CommunicationSetup = ref buffer.Struct<CommunicationSetup>(19);
            s7CommunicationSetup.Assert(FunctionCode.CommunicationSetup);

            pduSize = s7CommunicationSetup.PduSize;
            // TPKT + COTP DT + S7 PDU, assumes TPKT + COTP DT don't count as PDU data
            buffer = new byte[pduSize + 7];
        }

        private void ParseReadResponse(IReadOnlyCollection<IDataItem> dataItems, in int length)
        {
            DumpBuffer(length);

            ref var dt = ref buffer.Struct<Data>(4);
            dt.Assert();

            ref var s7Header = ref buffer.Struct<Header>(7);
            s7Header.Assert(MessageType.AckData);
            if (s7Header.ParamLength != 2) throw new Exception($"Read returned unexpected parameter length {s7Header.ParamLength}");

            ref var response = ref buffer.Struct<ReadRequest>(19);
            response.Assert((byte) dataItems.Count);

            if (length != s7Header.DataLength + 21)
                throw new Exception($"Length of response ({length}) does not match length of fixed part ({s7Header.ParamLength}) and data ({s7Header.DataLength}) of S7 Ack Data.");

            var data = buffer.AsSpan().Slice(21, s7Header.DataLength);
            List<Exception> exceptions = null;

            var offset = 0;
            foreach (var dataItem in dataItems)
            {
                // If the last item is odd length, there's no additional 0 padded. Slicing at the end of the loop
                // causes ArgumentOutOfRange in such conditions.
                data = data.Slice(offset);

                ref var di = ref data.Struct<DataItem>(0);
                if (di.ErrorCode == ReadWriteErrorCode.Success)
                {
                    var size = di.TransportSize.IsSizeInBytes() ? (int) di.Count : di.Count >> 3;
                    dataItem.ReadValue(data.Slice(4, size));

                    // Odd sizes are padded in the message
                    if (size % 2 == 1) size++;

                    offset = size + 4;
                }
                else
                {
                    if (exceptions == null) exceptions = new List<Exception>(1);

                    exceptions.Add(
                        new Exception($"Read of dataItem {dataItem} returned {di.ErrorCode}"));
                    offset = 4;
                }
            }

            if (exceptions != null) throw new AggregateException(exceptions);
        }

        private void ParseWriteResponse(IReadOnlyList<IDataItem> dataItems, in int length)
        {
            DumpBuffer(length);
            var span = buffer.AsSpan();

            ref var dt = ref span.Struct<Data>(4);
            dt.Assert();

            ref var s7Header = ref span.Struct<Header>(7);
            s7Header.Assert(MessageType.AckData);
            if (s7Header.ParamLength != 2)
                throw new Exception($"Write returned unexpected parameter length {s7Header.ParamLength}");

            if ((FunctionCode) span[19] != FunctionCode.Write)
                throw new Exception($"Expected FunctionCode {FunctionCode.Write}, received {(FunctionCode) span[19]}.");
            if (span[20] != dataItems.Count)
                throw new Exception($"Expected {dataItems.Count} items in write response, received {span[20]}.");

            if (length != s7Header.DataLength + 21)
                throw new Exception(
                    $"Length of response ({length}) does not match length of fixed part ({s7Header.ParamLength}) and data ({s7Header.DataLength}) of S7 Ack Data.");

            var errorCodes = MemoryMarshal.Cast<byte, ReadWriteErrorCode>(span.Slice(21));
            List<Exception> exceptions = null;

            for (var i = 0; i < dataItems.Count; i++)
            {
                if (errorCodes[i] == ReadWriteErrorCode.Success) continue;

                if (exceptions == null) exceptions = new List<Exception>(1);
                exceptions.Add(new Exception($"Read of dataItem {dataItems[i]} returned {errorCodes[i]}"));
            }
        }

        private void DumpBuffer(in int length, [CallerMemberName] string caller = null)
        {
            Console.WriteLine($"{caller}: {string.Join(", ", buffer.Take(length).Select(b => $"{b:X}"))}");
            Console.WriteLine($"{caller}: {string.Join(", ", buffer.Take(length).Select(b => b))}");
        }
    }
}