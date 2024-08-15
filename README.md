# VideoFrameToWriteableBitmap
[![NuGet version (DetoursCustomDPI)](https://img.shields.io/nuget/v/DetoursCustomDPI.svg?style=flat-square)](https://www.nuget.org/packages/DetoursCustomDPI/)

`VideoFrameToWriteableBitmap` is a library that uses FFmpeg.AutoGen to decode video frames and display them in a `WriteableBitmap` for WinUI applications. This library is designed to handle video codecs like H.264 and supports the processing and rendering of video frame data asynchronously to avoid blocking the UI thread.

## Features

- **Frame Decoding**: Decodes video frames using FFmpeg and converts them into a format suitable for display in WinUI.
- **WriteableBitmap Rendering**: Efficiently renders decoded frame data to a `WriteableBitmap`.
- **Thread Safety**: The library is designed with thread safety in mind, using locks to prevent concurrent access to unmanaged resources.

## Usage

### 1. FrameDecoder Class

The `FrameDecoder` class is responsible for decoding video frames using a specified codec. It manages the underlying FFmpeg structures and provides methods to feed raw frame data and render the decoded frames directly to a `WriteableBitmap`.

#### Example:
```csharp
// Initialize the decoder with the desired codec (e.g., H264)
var frameDecoder = new FrameDecoder(AVCodecID.AV_CODEC_ID_H264);

// Feed the decoder with raw video data (full NAL units for H264)
bool success = frameDecoder.Feed(frameData);

if (success)
{
    // Render the decoded frame data directly to the WriteableBitmap
    frameDecoder.Invalidate(writeableBitmap);
}
```

### 2. Disposing Resources

It's important to properly dispose of `FrameDecoder` objects to free unmanaged resources and prevent memory leaks.

#### Example:
```csharp
// Dispose of the FrameDecoder when done
frameDecoder.Dispose();
```

## License

This project is licensed under the LGPL 3.0 License. See the [LICENSE](LICENSE.txt) file for more details.
