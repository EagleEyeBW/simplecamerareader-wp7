﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Devices;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ZXing;
using ZXing.Common;

namespace zxingwp7.SimpleCameraReader
{
    /// <summary>
    /// A simple and easy to use wrapper for ZXing 2.0 for use with Windows Phone 7
    /// Written by Henning M. Stephansen
    /// http://www.henning.ms
    /// </summary>
    public class SimpleCameraReader
    {
        public delegate void DecodingCompletedEventHandler(object sender, Result result);

        public event DecodingCompletedEventHandler DecodingCompleted;

        public delegate void CameraInitializedEventHandler(object sender, bool initialized);

        public event CameraInitializedEventHandler CameraInitialized;

        private DispatcherTimer _timer;
        private PhotoCameraLuminanceSource _luminance;
        private MultiFormatReader _reader;
        private IBarcodeReader reader;
        private PhotoCamera _photoCamera;

        private TimeSpan _scanInterval;

        private bool _scanOnAutoFocus;
        private bool _initialized;

        private VideoBrush _surface;

        private Dispatcher uiDispatcher;

        /// <summary>
        /// Sets how often we should try to decode
        /// the camera feed for codes
        /// </summary>
        public double ScanInterval
        {
            get
            {
                return _scanInterval.TotalMilliseconds <= 0 ?  _scanInterval.TotalMilliseconds : 250;
            }
            set
            {
                _scanInterval = TimeSpan.FromMilliseconds(value);
            }
        }

        /// <summary>
        /// Gets the number of degrees that the viewfinder brush needs to be rotated 
        /// clockwise to align with the camera sensor
        /// </summary>
        public double CameraOrientation
        {
            get
            {
                return _photoCamera != null ? _photoCamera.Orientation : 0;
            }
        }

        /// <summary>
        /// Returns the PhotoCamera instance 
        /// </summary>
        public PhotoCamera Camera
        {
            get
            {
                return _photoCamera;
            }
        }

        /// <summary>
        /// Get or set the kind of Barcode formats the reader should try to
        /// decode
        /// </summary>
        public List<BarcodeFormat> Formats
        {
            get;
            set;
        }

        /// <summary>
        /// Sets whether the camera should scan when autofocused
        /// or on a timely fashion
        /// </summary>
        public bool ScanOnAutoFocus
        {
            get;
            set;
        }

        /// <summary>
        /// Get or set whether the camera should flash on auto-focus or not
        /// </summary>
        public FlashMode FlashMode
        {
            get
            {
                
                if (_initialized && _photoCamera != null)
                    return _photoCamera.FlashMode != FlashMode.Off ? _photoCamera.FlashMode : FlashMode.Off;

                return FlashMode.Off;
            }
            set
            {
                if (_photoCamera != null)
                    _photoCamera.FlashMode = value;
            }
        }

        /// <summary>
        /// Initializes the SimpleCameraReader
        /// </summary>
        /// <param name="scanOnAutoFocus">Sets whether the camera should scan on completed autofocus or on a timely fashion</param>
        public SimpleCameraReader(bool scanOnAutoFocus)
        {
            Initialize(null, scanOnAutoFocus);   
        }

        /// <summary>
        /// Initializes the SimpleCameraReader
        /// </summary>
        /// <param name="scanOnAutoFocus">Sets whether the camera should scan on completed autofocus or on a timely fashion</param>
        /// <param name="formats">A list of Barcode formats the camera should try to decode</param>
        public SimpleCameraReader(bool scanOnAutoFocus, List<BarcodeFormat> formats)
        {
            Initialize(formats, scanOnAutoFocus);
        }

        private void Initialize(List<BarcodeFormat> formats, bool scanOnAutoFocus)
        {
            ScanOnAutoFocus = scanOnAutoFocus;

            // Gets the Dispatcher for the current application so we can invoke the UI-Thread to get
            // preview-image and fire our timer events
            uiDispatcher = Application.Current.RootVisual.Dispatcher;

            InitializeCamera();

            _reader = new MultiFormatReader();

            // At first initialization we use all formats
//            if (formats == null) formats = BarcodeFormat.AllFormats;

            var hints = new Dictionary<DecodeHintType, Object> { { DecodeHintType.POSSIBLE_FORMATS, formats } };

            _reader.Hints = hints;

            
        }

        private void InitializeCamera()
        {
            _photoCamera = new PhotoCamera();
            _photoCamera.Initialized += OnPhotoCameraInitialized;
        }

        /// <summary>
        /// Starts the camera and capture process
        /// </summary>
        public void Start()
        {
            if (_photoCamera == null)
                InitializeCamera();

            // At this point if application exits etc. without proper stopping of camera
            // it will throw an Exception. 
            try
            {
                if (ScanOnAutoFocus)
                {
                    // Fired when Auto-Focus has completed => 
                    // start scanning the preview for codes
                    // and run a focus again (for
                    _photoCamera.AutoFocusCompleted += (o, arg) =>
                    {
                        uiDispatcher.BeginInvoke(ScanPreviewBuffer);

                        _photoCamera.Focus();
                    };

                    _photoCamera.Focus();
                }
                else
                {
                    // Invokes these method calls on the UI-thread
                    uiDispatcher.BeginInvoke(() =>
                    {
                        _timer = new DispatcherTimer();
                        _timer.Interval = _scanInterval;
                        _timer.Tick += (o, arg) => ScanPreviewBuffer();

                        CameraButtons.ShutterKeyHalfPressed += (o, arg) =>
                        {
                            _photoCamera.Focus();
                        };

                        _timer.Start();
                    });
                }
            }
            catch (Exception)
            {
                // Do nothing
            }
        }

        /// <summary>
        /// Stops the camera and capture process
        /// </summary>
        public void Stop()
        {
            if (_timer.IsEnabled)
                _timer.Stop();

            _photoCamera.Dispose();
            _photoCamera = null;
        }
        
        public void ShutterHalfPressed()
        {
            _photoCamera.Focus();
        }

        private void OnPhotoCameraInitialized(object sender, CameraOperationCompletedEventArgs e)
        {
            var width = Convert.ToInt32(_photoCamera.PreviewResolution.Width);
            var height = Convert.ToInt32(_photoCamera.PreviewResolution.Height);

            _luminance = new PhotoCameraLuminanceSource(width, height);

            _photoCamera.FlashMode = FlashMode.Off;

            _initialized = true;

            OnCameraInitialized(_initialized);
        }

        private void ScanPreviewBuffer()
        {
            if (_photoCamera == null) return;
            if (!_initialized) return;

            try
            {
                _photoCamera.GetPreviewBufferY(_luminance.PreviewBufferY);
                var binarizer = new HybridBinarizer(_luminance);
                var binBitmap = new BinaryBitmap(binarizer);

                var result = _reader.decode(binBitmap);
                if (result != null)
                    OnDecodingCompleted(result);
            }
            catch (Exception)
            {
                // If decoding fails it will throw a ReaderException
                // and we're not interested in doing anything with it
            }
        }

        /// <summary>
        /// When a decode was successful we get a result back
        /// </summary>
        /// <param name="result">Result of the decoding</param>
        protected virtual void OnDecodingCompleted(Result result)
        {
            if (DecodingCompleted != null)
                DecodingCompleted(this, result);
        }

        /// <summary>
        /// When camera is finished initializing and ready to go. Put Start after this event has fired
        /// </summary>
        /// <param name="initialized">A boolean value indicating whether camera was successfully initialized</param>
        protected virtual void OnCameraInitialized(bool initialized)
        {
            if (CameraInitialized != null)
                CameraInitialized(this, initialized);
        }
    }

    public class PhotoCameraLuminanceSource : BaseLuminanceSource
    {
        public byte[] PreviewBufferY
        {
            get
            {
                return luminances;
            }
        }

        public PhotoCameraLuminanceSource(int width, int height)
            : base(width, height)
        {
            luminances = new byte[width * height];
        }

        internal PhotoCameraLuminanceSource(int width, int height, byte[] newLuminances)
            : base(width, height)
        {
            luminances = newLuminances;
        }

        protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        {
            return new PhotoCameraLuminanceSource(width, height, newLuminances);
        }
    }
}
