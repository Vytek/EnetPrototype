using System;
using ENet;
using NetStack.Compression;
using NetStack.Serialization;
using SoL.Networking.Objects;
using SoL.Networking.Replication;
using UnityEngine;

namespace SoL.Networking
{
    public struct PacketHeader
    {
        public uint Id;
        public OpCodes OpCode;
    }

    public static class EventExtensions
    {
        public static BitBuffer GetBufferFromPacket(this Packet packet, BitBuffer buffer = null)
        {
            var data = ByteArrayPool.GetByteArray(packet.Length + 4);
            
            packet.CopyTo(data);

            if (buffer == null)
            {
                buffer = new BitBuffer(128);                
            }
            else
            {
                buffer.Clear();
            }

            buffer.FromArray(data, packet.Length);
            ByteArrayPool.ReturnByteArray(data);

            return buffer;
        }
    }
    
    public static class BitBufferExtensions
    {
        public static Packet GetPacketFromBuffer(this BitBuffer buffer, PacketFlags flags = PacketFlags.None)
        {
            var data = ByteArrayPool.GetByteArray(buffer.Length + 4);
            buffer.ToArray(data);
            Packet packet = default(Packet);
            packet.Create(data, flags);
            ByteArrayPool.ReturnByteArray(data);
            return packet;
        }
        
        [Obsolete("Use NetworkEntity instead!")]
        public static BitBuffer AddEntityHeader(this BitBuffer buffer, Peer peer, OpCodes opCode, bool clearBuffer = true)
        {
            if (clearBuffer)
            {
                buffer.Clear();   
            }
            buffer.AddUShort((ushort) opCode).AddUInt(peer.ID);
            return buffer;
        }

        public static BitBuffer AddEntityHeader(this BitBuffer buffer, NetworkEntity entity, OpCodes opCode, bool clearBuffer = true)
        {
            if (clearBuffer)
            {
                buffer.Clear();   
            }
            buffer.AddUShort((ushort) opCode).AddUInt(entity.NetworkId.Value);
            return buffer;            
        }
        
        public static PacketHeader GetEntityHeader(this BitBuffer buffer)
        {
            PacketHeader header = default(PacketHeader);
            header.OpCode = (OpCodes)buffer.ReadUShort();
            header.Id = buffer.ReadUInt();
            return header;
        }

        public static BitBuffer AddSyncVar(this BitBuffer buffer, ISynchronizedVariable value, bool resetDirty = true)
        {
            value.PackVariable(buffer);
            if (resetDirty)
            {
                value.ResetDirty();
            }
            return buffer;
        }

        public static void ReadSyncVar(this BitBuffer buffer, ISynchronizedVariable var)
        {
            var.ReadVariable(buffer);
        }
        
        public static BitBuffer AddUShort(this BitBuffer buffer, ushort value)
        {
            buffer.AddUInt(value);
            return buffer;
        }

        public static ushort ReadUShort(this BitBuffer buffer)
        {
            return (ushort) buffer.ReadUInt();
        }

        public static BitBuffer AddFloat(this BitBuffer buffer, float value)
        {
            buffer.AddUInt(new UIntFloat {floatValue = value }.uintValue);
            return buffer;
        }

        public static float ReadFloat(this BitBuffer buffer)
        {
            return new UIntFloat {uintValue = buffer.ReadUInt()}.floatValue;
        }

        public static BitBuffer AddVector3(this BitBuffer buffer, Vector3 value, BoundedRange[] range)
        {
            var compressed = BoundedRange.Compress(value, range);
            buffer.AddUInt(compressed.x).AddUInt(compressed.y).AddUInt(compressed.z);
            return buffer;
        }

        public static Vector3 ReadVector3(this BitBuffer buffer, BoundedRange[] range)
        {
            var compressed = new CompressedVector3(
                buffer.ReadUInt(),
                buffer.ReadUInt(),
                buffer.ReadUInt());
            return BoundedRange.Decompress(compressed, range);
        }        

        public static BitBuffer AddInitialState(this BitBuffer buffer, NetworkEntity netEntity)
        {
            return netEntity.AddInitialState(buffer);
        }
    }
}