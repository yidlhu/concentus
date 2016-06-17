﻿using Concentus;
using Concentus.Celt;
using Concentus.Common.CPlusPlus;
using Concentus.Opus;
using Concentus.Opus.Enums;
using Concentus.Opus.Structs;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestOpusEncode
{
    public class Program
    {
        private const int MAX_PACKET = 1500;
        private const int SAMPLES = 48000 * 30;
        private const int SSAMPLES = (SAMPLES / 3);
        private const int MAX_FRAME_SAMP = 5760;

        private static uint iseed;

        internal static void deb2_impl(Pointer<byte>_t, BoxedValue<Pointer<byte>> _p, int _k, int _x, int _y)
        {
            int i;
            if (_x > 2)
            {
                if (_y < 3)
                {
                    for (i = 0; i < _y; i++)
                    {
                        // *(--*_p)=_t[i+1];
                        _p.Val = _p.Val.Point(-1); // fixme: aaaaagggg
                        _p.Val[0] = _t[i + 1];
                    }
                }
            }
            else {
                _t[_x] = _t[_x - _y];
                deb2_impl(_t, _p, _k, _x + 1, _y);
                for (i = _t[_x - _y] + 1; i < _k; i++)
                {
                    _t[_x] = (byte)i;
                    deb2_impl(_t, _p, _k, _x + 1, _x);
                }
            }
        }

        /*Generates a De Bruijn sequence (k,2) with length k^2*/
        internal static void debruijn2(int _k, Pointer<byte> _res)
        {
            BoxedValue<Pointer<byte>> p;
            Pointer<byte> t;
            t = Pointer.Malloc<byte>(_k * 2);
            t.MemSet(0, _k * 2);
            p = new BoxedValue<Pointer<byte>>(_res.Point(_k * _k));
            deb2_impl(t, p, _k, 1, 1);
        }

        /*MWC RNG of George Marsaglia*/
        private static uint Rz, Rw;
        internal static uint fast_rand()
        {
            Rz = 36969 * (Rz & 65535) + (Rz >> 16);
            Rw = 18000 * (Rw & 65535) + (Rw >> 16);
            return (Rz << 16) + Rw;
        }

        internal static void generate_music(Pointer<short> buf, int len)
        {
            int a1, b1, a2, b2;
            int c1, c2, d1, d2;
            int i, j;
            a1 = b1 = a2 = b2 = 0;
            c1 = c2 = d1 = d2 = 0;
            j = 0;
            /*60ms silence*/
            //for(i=0;i<2880;i++)buf[i*2]=buf[i*2+1]=0;
            for (i = 0; i < len; i++)
            {
                uint r;
                int v1, v2;
                v1 = v2 = (((j * ((j >> 12) ^ ((j >> 10 | j >> 12) & 26 & j >> 7))) & 128) + 128) << 15;
                r = fast_rand();
                v1 += (int)r & 65535;
                v1 -= (int)r >> 16;
                r = fast_rand();
                v2 += (int)r & 65535;
                v2 -= (int)r >> 16;
                b1 = v1 - a1 + ((b1 * 61 + 32) >> 6); a1 = v1;
                b2 = v2 - a2 + ((b2 * 61 + 32) >> 6); a2 = v2;
                c1 = (30 * (c1 + b1 + d1) + 32) >> 6; d1 = b1;
                c2 = (30 * (c2 + b2 + d2) + 32) >> 6; d2 = b2;
                v1 = (c1 + 128) >> 8;
                v2 = (c2 + 128) >> 8;
                buf[i * 2] = (short)(v1 > 32767 ? 32767 : (v1 < -32768 ? -32768 : v1));
                buf[i * 2 + 1] = (short)(v2 > 32767 ? 32767 : (v2 < -32768 ? -32768 : v2));
                if (i % 6 == 0) j++;
            }
        }

        internal static void test_failed()
        {
            Console.WriteLine("Test FAILED!");
            if (Debugger.IsAttached)
            {
                throw new Exception();
            }
        }

        internal static readonly int[] fsizes = { 960 * 3, 960 * 2, 120, 240, 480, 960 };
        internal static readonly string[] mstrings = { "    LP", "Hybrid", "  MDCT" };

        internal static int run_test1(bool no_fuzz)
        {
            
            byte[] mapping/*[256]*/ = { 0, 1, 255 };
            byte[] db62 = new byte[36];
            int i;
            int rc, j;
            BoxedValue<int> err = new BoxedValue<int>();
            OpusEncoder enc;
            OpusMSEncoder MSenc;
            OpusDecoder dec;
            OpusMSDecoder MSdec;
            OpusMSDecoder MSdec_err;
            OpusDecoder[] dec_err = new OpusDecoder[10];
            Pointer<short> inbuf;
            Pointer<short> outbuf;
            Pointer<short> out2buf;
            int bitrate_bps;
            Pointer<byte> packet = Pointer.Malloc<byte>(MAX_PACKET + 257);
            uint enc_final_range;
            uint dec_final_range;
            int fswitch;
            int fsize;
            int count;

            /*FIXME: encoder api tests, fs!=48k, mono, VBR*/

            Console.WriteLine("  Encode+Decode tests.");

            enc = opus_encoder.opus_encoder_create(48000, 2, OpusApplication.OPUS_APPLICATION_VOIP, err);
            if (err.Val != OpusError.OPUS_OK || enc == null) test_failed();

            for (i = 0; i < 2; i++)
            {
                BoxedValue<int> ret_err = i != 0 ? null : err;
                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 2, 2, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_UNIMPLEMENTED, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();

                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 0, 1, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_VOIP, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();

                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(44100, 2, 2, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_VOIP, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();

                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 2, 2, 3, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_VOIP, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();

                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 2, -1, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_VOIP, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();

                MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 256, 2, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_VOIP, ret_err);
                if ((ret_err != null && ret_err.Val != OpusError.OPUS_BAD_ARG) || MSenc != null) test_failed();
            }

            MSenc = opus_multistream_encoder.opus_multistream_encoder_create(8000, 2, 2, 0, mapping.GetPointer(), OpusApplication.OPUS_APPLICATION_AUDIO, err);
            if (err.Val != OpusError.OPUS_OK || MSenc == null) test_failed();

            /*Some multistream encoder API tests*/
            i = MSenc.GetBitrate();
            i = MSenc.GetLSBDepth();
            if (i < 16) test_failed();

            {
                OpusEncoder tmp_enc;
                tmp_enc = MSenc.GetMultistreamEncoderState(1);
                if (tmp_enc == null) test_failed();
                j = tmp_enc.GetLSBDepth();
                if (i != j) test_failed();
                try
                {
                    MSenc.GetMultistreamEncoderState(2);
                    test_failed();
                }
                catch (ArgumentException e) { }
            }

            dec = opus_decoder.opus_decoder_create(48000, 2, err);
            if (err.Val != OpusError.OPUS_OK || dec == null) test_failed();

            MSdec = opus_multistream_decoder.opus_multistream_decoder_create(48000, 2, 2, 0, mapping.GetPointer(), err);
            if (err.Val != OpusError.OPUS_OK || MSdec == null) test_failed();

            MSdec_err = opus_multistream_decoder.opus_multistream_decoder_create(48000, 3, 2, 0, mapping.GetPointer(), err);
            if (err.Val != OpusError.OPUS_OK || MSdec_err == null) test_failed();

            // fixme: this tests assign() performed on a decoder struct, which doesn't exist
            //dec_err[0] = (OpusDecoder*)malloc(opus_decoder_get_size(2));
            //memcpy(dec_err[0], dec, opus_decoder_get_size(2));
            dec_err[0] = opus_decoder.opus_decoder_create(48000, 2, err);
            dec_err[1] = opus_decoder.opus_decoder_create(48000, 1, err);
            dec_err[2] = opus_decoder.opus_decoder_create(24000, 2, err);
            dec_err[3] = opus_decoder.opus_decoder_create(24000, 1, err);
            dec_err[4] = opus_decoder.opus_decoder_create(16000, 2, err);
            dec_err[5] = opus_decoder.opus_decoder_create(16000, 1, err);
            dec_err[6] = opus_decoder.opus_decoder_create(12000, 2, err);
            dec_err[7] = opus_decoder.opus_decoder_create(12000, 1, err);
            dec_err[8] = opus_decoder.opus_decoder_create(8000, 2, err);
            dec_err[9] = opus_decoder.opus_decoder_create(8000, 1, err);
            for (i = 1; i < 10; i++) if (dec_err[i] == null) test_failed();

            //{
            //    OpusEncoder* enccpy;
            //    /*The opus state structures contain no pointers and can be freely copied*/
            //    enccpy = (OpusEncoder*)malloc(opus_encoder_get_size(2));
            //    memcpy(enccpy, enc, opus_encoder_get_size(2));
            //    memset(enc, 255, opus_encoder_get_size(2));
            //    opus_encoder_destroy(enc);
            //    enc = enccpy;
            //}

            inbuf = Pointer.Malloc<short>(SAMPLES * 2);
            outbuf = Pointer.Malloc<short>(SAMPLES * 2);
            out2buf = Pointer.Malloc<short>(MAX_FRAME_SAMP * 3);
            if (inbuf == null || outbuf == null || out2buf == null) test_failed();

            generate_music(inbuf, SAMPLES);

            ///*   FILE *foo;
            //foo = fopen("foo.sw", "wb+");
            //fwrite(inbuf, 1, SAMPLES*2*2, foo);
            //fclose(foo);*/

            enc.SetBandwidth(OpusConstants.OPUS_AUTO);

            for (rc = 0; rc < 3; rc++)
            {
                enc.SetVBR(rc < 2);
                enc.SetVBRConstraint(rc == 1);
                enc.SetUseInbandFEC(rc == 0);

                int[] modes = { 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 };
                int[] rates = { 6000, 12000, 48000, 16000, 32000, 48000, 64000, 512000, 13000, 24000, 48000, 64000, 96000 };
                int[] frame = { 960 * 2, 960, 480, 960, 960, 960, 480, 960 * 3, 960 * 3, 960, 480, 240, 120 };

                for (j = 0; j < modes.Length; j++)
                {
                    int rate;
                    rate = rates[j] + (int)fast_rand() % rates[j];
                    count = i = 0;
                    do
                    {
                        int bw, len, out_samples, frame_size;
                        frame_size = frame[j];
                        if ((fast_rand() & 255) == 0)
                        {
                            enc.ResetState();
                            dec.ResetState();

                            if ((fast_rand() & 1) != 0)
                            {
                                dec_err[fast_rand() & 1].ResetState();
                            }
                        }

                        if ((fast_rand() & 127) == 0)
                        {
                            dec_err[fast_rand() & 1].ResetState();
                        }

                        if (fast_rand() % 10 == 0)
                        {
                            int complex = (int)(fast_rand() % 11);
                            enc.SetComplexity(complex);
                        }

                        if (fast_rand() % 50 == 0)
                        {
                            dec.ResetState();
                        }

                        enc.SetUseInbandFEC(rc == 0);
                        enc.SetForceMode(OpusMode.MODE_SILK_ONLY + modes[j]);
                        enc.SetUseDTX((fast_rand() & 1) != 0);
                        enc.SetBitrate(rate);
                        enc.SetForceChannels(rates[j] >= 64000 ? 2 : 1);
                        enc.SetComplexity((count >> 2) % 11);
                        enc.SetPacketLossPercent((int)((fast_rand() & 15) & (fast_rand() % 15)));

                        bw = modes[j] == 0 ? OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND + (int)(fast_rand() % 3) :
                            modes[j] == 1 ? OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND + (int)(fast_rand() & 1) :
                            OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND + (int)(fast_rand() % 5);
                    
                        if (modes[j] == 2 && bw == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
                            bw += 3;
                        enc.SetBandwidth(bw);
                        len = opus_encoder.opus_encode(enc, inbuf.Point(i << 1), frame_size, packet, MAX_PACKET);
                        if (len < 0 || len > MAX_PACKET) test_failed();
                        enc_final_range = enc.GetFinalRange();
                        if ((fast_rand() & 3) == 0)
                        {
                            if (Repacketizer.opus_packet_pad(packet, len, len + 1) != OpusError.OPUS_OK) test_failed();
                            len++;
                        }
                        if ((fast_rand() & 7) == 0)
                        {
                            if (Repacketizer.opus_packet_pad(packet, len, len + 256) != OpusError.OPUS_OK) test_failed();
                            len += 256;
                        }
                        if ((fast_rand() & 3) == 0)
                        {
                            len = Repacketizer.opus_packet_unpad(packet, len);
                            if (len < 1) test_failed();
                        }
                        out_samples = opus_decoder.opus_decode(dec, packet, len, outbuf.Point(i << 1), MAX_FRAME_SAMP, 0);
                        if (out_samples != frame_size) test_failed();
                        dec_final_range = dec.GetFinalRange();
                        if (enc_final_range != dec_final_range) test_failed();
                        /*LBRR decode*/
                        out_samples = opus_decoder.opus_decode(dec_err[0], packet, len, out2buf, frame_size, ((int)fast_rand() & 3) != 0 ? 1 : 0);
                        if (out_samples != frame_size) test_failed();
                        out_samples = opus_decoder.opus_decode(dec_err[1], packet, (fast_rand() & 3) == 0 ? 0 : len, out2buf, MAX_FRAME_SAMP, ((int)fast_rand() & 7) != 0 ? 1 : 0);
                        if (out_samples < 120) test_failed();
                        i += frame_size;
                        count++;
                    } while (i < (SSAMPLES - MAX_FRAME_SAMP));
                    Console.WriteLine("    Mode {0} FB encode {1}, {2} bps OK.", mstrings[modes[j]], rc == 0 ? " VBR" : rc == 1 ? "CVBR" : " CBR", rate);
                }
            }

            //if (opus_encoder_ctl(enc, OPUS_SET_FORCE_MODE(OPUS_AUTO)) != OpusError.OPUS_OK) test_failed();
            //if (opus_encoder_ctl(enc, OPUS_SET_FORCE_CHANNELS(OPUS_AUTO)) != OpusError.OPUS_OK) test_failed();
            //if (opus_encoder_ctl(enc, OPUS_SET_INBAND_FEC(0)) != OpusError.OPUS_OK) test_failed();
            //if (opus_encoder_ctl(enc, OPUS_SET_DTX(0)) != OpusError.OPUS_OK) test_failed();

            //for (rc = 0; rc < 3; rc++)
            //{
            //    if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_VBR(rc < 2)) != OpusError.OPUS_OK) test_failed();
            //    if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_VBR_CONSTRAINT(rc == 1)) != OpusError.OPUS_OK) test_failed();
            //    if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_VBR_CONSTRAINT(rc == 1)) != OpusError.OPUS_OK) test_failed();
            //    if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_INBAND_FEC(rc == 0)) != OpusError.OPUS_OK) test_failed();
            //    for (j = 0; j < 16; j++)
            //    {
            //        int rate;
            //        int modes[16] = { 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2, 2, 2 };
            //        int rates[16] = { 4000, 12000, 32000, 8000, 16000, 32000, 48000, 88000, 4000, 12000, 32000, 8000, 16000, 32000, 48000, 88000 };
            //        int frame[16] = { 160 * 1, 160, 80, 160, 160, 80, 40, 20, 160 * 1, 160, 80, 160, 160, 80, 40, 20 };
            //        if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_INBAND_FEC(rc == 0 && j == 1)) != OpusError.OPUS_OK) test_failed();
            //        if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_FORCE_MODE(MODE_SILK_ONLY + modes[j])) != OpusError.OPUS_OK) test_failed();
            //        rate = rates[j] + fast_rand() % rates[j];
            //        if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_DTX(fast_rand() & 1)) != OpusError.OPUS_OK) test_failed();
            //        if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_BITRATE(rate)) != OpusError.OPUS_OK) test_failed();
            //        count = i = 0;
            //        do
            //        {
            //            int pred, len, out_samples, frame_size, loss;
            //            if (opus_multistream_encoder_ctl(MSenc, OPUS_GET_PREDICTION_DISABLED(&pred)) != OpusError.OPUS_OK) test_failed();
            //            if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_PREDICTION_DISABLED((int)(fast_rand() & 15) < (pred ? 11 : 4))) != OpusError.OPUS_OK) test_failed();
            //            frame_size = frame[j];
            //            if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_COMPLEXITY((count >> 2) % 11)) != OpusError.OPUS_OK) test_failed();
            //            if (opus_multistream_encoder_ctl(MSenc, OPUS_SET_PACKET_LOSS_PERC((fast_rand() & 15) & (fast_rand() % 15))) != OpusError.OPUS_OK) test_failed();
            //            if ((fast_rand() & 255) == 0)
            //            {
            //                if (opus_multistream_encoder_ctl(MSenc, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //                if (opus_multistream_decoder_ctl(MSdec, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //                if ((fast_rand() & 3) != 0)
            //                {
            //                    if (opus_multistream_decoder_ctl(MSdec_err, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //                }
            //            }
            //            if ((fast_rand() & 255) == 0)
            //            {
            //                if (opus_multistream_decoder_ctl(MSdec_err, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //            }
            //            len = opus_multistream_encode(MSenc, &inbuf[i << 1], frame_size, packet, MAX_PACKET);
            //            if (len < 0 || len > MAX_PACKET) test_failed();
            //            if (opus_multistream_encoder_ctl(MSenc, OPUS_GET_FINAL_RANGE(&enc_final_range)) != OpusError.OPUS_OK) test_failed();
            //            if ((fast_rand() & 3) == 0)
            //            {
            //                if (opus_multistream_packet_pad(packet, len, len + 1, 2) != OpusError.OPUS_OK) test_failed();
            //                len++;
            //            }
            //            if ((fast_rand() & 7) == 0)
            //            {
            //                if (opus_multistream_packet_pad(packet, len, len + 256, 2) != OpusError.OPUS_OK) test_failed();
            //                len += 256;
            //            }
            //            if ((fast_rand() & 3) == 0)
            //            {
            //                len = opus_multistream_packet_unpad(packet, len, 2);
            //                if (len < 1) test_failed();
            //            }
            //            out_samples = opus_multistream_decode(MSdec, packet, len, out2buf, MAX_FRAME_SAMP, 0);
            //            if (out_samples != frame_size * 6) test_failed();
            //            if (opus_multistream_decoder_ctl(MSdec, OPUS_GET_FINAL_RANGE(&dec_final_range)) != OpusError.OPUS_OK) test_failed();
            //            if (enc_final_range != dec_final_range) test_failed();
            //            /*LBRR decode*/
            //            loss = (fast_rand() & 63) == 0;
            //            out_samples = opus_multistream_decode(MSdec_err, packet, loss ? 0 : len, out2buf, frame_size * 6, (fast_rand() & 3) != 0);
            //            if (out_samples != (frame_size * 6)) test_failed();
            //            i += frame_size;
            //            count++;
            //        } while (i < (SSAMPLES / 12 - MAX_FRAME_SAMP));
            //        fprintf(stdout, "    Mode %s NB dual-mono MS encode %s, %6d bps OK.\n", mstrings[modes[j]], rc == 0 ? " VBR" : rc == 1 ? "CVBR" : " CBR", rate);
            //    }
            //}

            //bitrate_bps = 512000;
            //fsize = fast_rand() % 31;
            //fswitch = 100;

            //debruijn2(6, db62);
            //count = i = 0;
            //do
            //{
            //    unsigned char toc;
            //    const unsigned char* frames[48];
            //    short size[48];
            //    int payload_offset;
            //    opus_uint32 dec_final_range2;
            //    int jj, dec2;
            //    int len, out_samples;
            //    int frame_size = fsizes[db62[fsize]];
            //    opus_int32 offset = i % (SAMPLES - MAX_FRAME_SAMP);

            //    opus_encoder_ctl(enc, OPUS_SET_BITRATE(bitrate_bps));

            //    len = opus_encode(enc, &inbuf[offset << 1], frame_size, packet, MAX_PACKET);
            //    if (len < 0 || len > MAX_PACKET) test_failed();
            //    count++;

            //    opus_encoder_ctl(enc, OPUS_GET_FINAL_RANGE(&enc_final_range));

            //    out_samples = opus_decode(dec, packet, len, &outbuf[offset << 1], MAX_FRAME_SAMP, 0);
            //    if (out_samples != frame_size) test_failed();

            //    opus_decoder_ctl(dec, OPUS_GET_FINAL_RANGE(&dec_final_range));

            //    /* compare final range encoder rng values of encoder and decoder */
            //    if (dec_final_range != enc_final_range) test_failed();

            //    /* We fuzz the packet, but take care not to only corrupt the payload
            //    Corrupted headers are tested elsewhere and we need to actually run
            //    the decoders in order to compare them. */
            //    if (opus_packet_parse(packet, len, &toc, frames, size, &payload_offset) <= 0) test_failed();
            //    if ((fast_rand() & 1023) == 0) len = 0;
            //    for (j = (frames[0] - packet); j < len; j++) for (jj = 0; jj < 8; jj++) packet[j] ^= ((!no_fuzz) && ((fast_rand() & 1023) == 0)) << jj;
            //    out_samples = opus_decode(dec_err[0], len > 0 ? packet : null, len, out2buf, MAX_FRAME_SAMP, 0);
            //    if (out_samples < 0 || out_samples > MAX_FRAME_SAMP) test_failed();
            //    if ((len > 0 && out_samples != frame_size)) test_failed(); /*FIXME use lastframe*/

            //    opus_decoder_ctl(dec_err[0], OPUS_GET_FINAL_RANGE(&dec_final_range));

            //    /*randomly select one of the decoders to compare with*/
            //    dec2 = fast_rand() % 9 + 1;
            //    out_samples = opus_decode(dec_err[dec2], len > 0 ? packet : null, len, out2buf, MAX_FRAME_SAMP, 0);
            //    if (out_samples < 0 || out_samples > MAX_FRAME_SAMP) test_failed(); /*FIXME, use factor, lastframe for loss*/

            //    opus_decoder_ctl(dec_err[dec2], OPUS_GET_FINAL_RANGE(&dec_final_range2));
            //    if (len > 0 && dec_final_range != dec_final_range2) test_failed();

            //    fswitch--;
            //    if (fswitch < 1)
            //    {
            //        int new_size;
            //        fsize = (fsize + 1) % 36;
            //        new_size = fsizes[db62[fsize]];
            //        if (new_size == 960 || new_size == 480) fswitch = 2880 / new_size * (fast_rand() % 19 + 1);
            //        else fswitch = (fast_rand() % (2880 / new_size)) + 1;
            //    }
            //    bitrate_bps = ((fast_rand() % 508000 + 4000) + bitrate_bps) >> 1;
            //    i += frame_size;
            //} while (i < SAMPLES * 4);
            //fprintf(stdout, "    All framesize pairs switching encode, %d frames OK.\n", count);

            //if (opus_encoder_ctl(enc, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //opus_encoder_destroy(enc);
            //if (opus_multistream_encoder_ctl(MSenc, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //opus_multistream_encoder_destroy(MSenc);
            //if (opus_decoder_ctl(dec, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();
            //opus_decoder_destroy(dec);
            //if (opus_multistream_decoder_ctl(MSdec, OPUS_RESET_STATE) != OpusError.OPUS_OK) test_failed();

            return 0;
        }

        internal static void Main(string[] args)
        {
            iseed = (uint)new Random().Next();

            Rw = Rz = iseed;

            string oversion = OpusPacket.opus_get_version_string();

            Console.WriteLine("Testing {0} encoder. Random seed: {1}", oversion, iseed);
            run_test1(true);

            Console.WriteLine("Tests completed successfully.");
        }
    }
}
