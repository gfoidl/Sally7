﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sally7.Protocol.IsoOverTcp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Tpkt
    {
        private const byte IsoVersion = 3;

        public byte Version;
        public byte Reserved;
        public BigEndianShort Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Assert()
        {
            if (Version != IsoVersion)
            {
                Throw();
                static void Throw() => throw new Exception("Spec violation: TPKT header has incorrect version.");
            }

            if (Reserved != 0)
            {
                Throw();
                static void Throw() => throw new Exception("Spec violoation: TPKT reserved is not 0.");
            }

            if (Length.High == 0 && Length.Low < 7)
            {
                Throw();
                static void Throw() => throw new Exception("Spec violation: TPKT length is smaller than 7.");
            }
        }

        public void Init(BigEndianShort length)
        {
            Version = IsoVersion;
            Reserved = 0;
            Length = length;
        }

        public readonly int MessageLength() => Length - 4;
    }
}