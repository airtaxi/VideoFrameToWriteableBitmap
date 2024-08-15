using FFmpeg.AutoGen;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;

namespace VideoFrameToWriteableBitmap;

/// <summary>
/// Since unsafe context cannot await, we need to use a helper class to write the frame data to the WriteableBitmap.
/// </summary>
internal static class FrameWriter
{
    /// <summary>
    /// Write the pixel data to the WriteableBitmap.
    /// </summary>
    /// <param name="writeableBitmap"></param>
    /// <param name="pixelData"></param>
    public static void InvalidateWriteableBitmap(WriteableBitmap writeableBitmap, byte[] pixelData)
    {
        // Using Task to avoid blocking the UI thread (Video frame data is usually large)
        writeableBitmap.DispatcherQueue.TryEnqueue(async () =>
        {
            using (var stream = writeableBitmap.PixelBuffer.AsStream())
            {
                await Task.Run(() => stream.Write(pixelData, 0, pixelData.Length));
            }
            writeableBitmap.Invalidate();
        });
    }
}

public unsafe class FrameDecoder : IDisposable
{
    #region private fields
    // Unmanaged resources
    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;
    private SwsContext* _swsContext;

    // Lock object (to prevent concurrent access to the unmanaged resources)
    private object _padlock = new();

    // Disposed flag
    private bool _disposed;
    #endregion

    /// <summary>
    /// Create a new FrameDecoder with the specified codec ID.
    /// </summary>
    /// <param name="codecId"></param>
    /// <exception cref="ApplicationException"></exception>
    public FrameDecoder(AVCodecID codecId)
    {
        var codec = ffmpeg.avcodec_find_decoder(codecId);
        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new ApplicationException("Could not open codec.");
    }

    /// <summary>
    /// Feed the decoder with a new frame. (Using RtspClientSharp is recommended)
    /// (IMPORTANT) If codec is H264, the frame must be a full NAL unit (I frame). Otherwise, the decoder will not work.
    /// </summary>
    /// <param name="data"> The frame data. </param>
    /// <returns> True if the frame is successfully decoded. </returns>
    public bool Feed(byte[] data)
    {
        lock (_padlock)
        {
            if (_disposed) return false;

            fixed (byte* pData = data)
            {
                // Clear the packet
                ffmpeg.av_packet_unref(_packet);

                // Feed the decoder with the new packet
                _packet->data = pData;
                _packet->size = data.Length;

                // Decode the packet
                var result = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (result < 0)
                {
                    Debug.WriteLine($"Packet Decoding Error: {result} ({data.Length} bytes)");
                    return false;
                }

                // Decode the frame
                result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
                if (result < 0)
                {
                    Debug.WriteLine($"Frame Decoding Error: {result} ({data.Length} bytes)");
                    return false;
                }
            }

            // We're successful. Return true.
            return true;
        }
    }

    /// <summary>
    /// If needed, get the current AVFrame.
    /// </summary>
    /// <returns></returns>
    public AVFrame* GetCurrentFrame()
    {
        lock (_padlock)
        {
            if (_disposed) return null; // Return null if the object is disposed
            return _frame;
        }
    }

    /// <summary>
    /// If needed, get the current AVPacket.
    /// </summary>
    /// <returns></returns>
    public AVPacket* GetCurrentPacket()
    {
        lock (_padlock)
        {
            if (_disposed) return null; // Return null if the object is disposed
            return _packet;
        }
    }

    /// <summary>
    /// Invalidate the WriteableBitmap with the current frame data.
    /// </summary>
    /// <param name="writeableBitmap"></param>
    public void Invalidate(WriteableBitmap writeableBitmap)
    {
        lock (_padlock)
        {
            if (_disposed) return;

            // Get the frame width and height
            int width = _frame->width;
            int height = _frame->height;

            // If frame is not yet decoded, return
            if (width + height == 0) return;

            // WinUI WriteableBitmap requires BGRA format
            var destFormat = AVPixelFormat.AV_PIX_FMT_BGRA;

            // Initialize the SwsContext if not declared
            if (_swsContext == null)
            {
                _swsContext = ffmpeg.sws_getContext(width, height, (AVPixelFormat)_frame->format,
                    width, height, destFormat,
                    ffmpeg.SWS_BILINEAR, null, null, null);
            }

            // Allocate the destination data
            var dstData = new byte[width * height * 4];
            var dstLinesize = new int[] { 4 * width };

            // Scale the frame data to the destination data
            fixed (byte* pDstData = dstData)
            {
                fixed (int* pDstLinesize = dstLinesize)
                {
                    byte_ptrArray4 dstDataArray = new();
                    dstDataArray[0] = pDstData;

                    int_array4 dstLinesizeArray = new();
                    dstLinesizeArray[0] = dstLinesize[0];

                    ffmpeg.sws_scale(_swsContext, _frame->data, _frame->linesize, 0, height, dstDataArray, dstLinesizeArray);
                }
            }

            // Write the frame data to the WriteableBitmap (awaiting is not possible here)
            FrameWriter.InvalidateWriteableBitmap(writeableBitmap, dstData);
        }
    }

    /// <summary>
    /// Free the unmanaged resources.
    /// </summary>
    public unsafe void Dispose()
    {
        lock (_padlock)
        {
            _disposed = true;

            // Free the codec context
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);

            // Free the frame
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);

            // Free the packet
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);

            // Free the sws context if declared
            if (_swsContext != null) ffmpeg.sws_freeContext(_swsContext);
        }

        // Suppress finalization
        GC.SuppressFinalize(this);
    }
}
