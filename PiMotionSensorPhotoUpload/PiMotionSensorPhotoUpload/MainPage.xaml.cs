using System;
using Windows.Devices.Gpio;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

//Azure
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

//Camera
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace PiMotionSensorPhotoUpload
{
    public sealed partial class MainPage : Page
    {
        //Azure Account Values
        //TODO:  - substitute your 3 actual values below (!)
        private readonly string Azure_StorageAccountName = "<YOUR Azure_StorageAccountName>";
        private readonly string Azure_ContainerName = "<YOUR Azure_ContainerName>";
        private readonly string Azure_AccessKey = "<YOUR Azure_ContainerName>";

        //Status LED variables
        private const int LED_PIN = 5;
        private GpioPin PinLED;

        //PIR Motion Detector variables
        private const int PIR_PIN = 16;
        private GpioPin PinPIR;

        //Webcam variables
        private MediaCapture MediaCap;
        private bool IsInPictureCaptureMode = false;

        /// <summary>
        /// Entry point of the application
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            InitilizeWebcam();

            InitializeGPIO();

            //Turn the Status LED on
            LightLED(true);

            // At this point, the application waits for motion to be detected by
            // the PIR sensor, which then calls the PinPIR_ValueChanged() fucntion
        }

        #region GPIO code (LED & PIR)

        /// <summary>
        /// Initialize the GPIO ports on the Raspberry Pi
        /// 
        /// GPIO PIN 16 = PIR Signal
        /// GPIO PIN  5 = LED Status
        /// </summary>
        private void InitializeGPIO()
        {
            try
            {
                //Obtain a reference to the GPIO Controller
                var gpio = GpioController.GetDefault();

                // Show an error if there is no GPIO controller
                if (gpio == null)
                {
                    PinLED = null;
                    GpioStatus.Text = "No GPIO controller found on this device.";
                    return;
                }

                //Open the GPIO port for LED
                PinLED = gpio.OpenPin(LED_PIN);

                //set the mode as Output (we are WRITING a signal to this port)
                PinLED.SetDriveMode(GpioPinDriveMode.Output);

                //Open the GPIO port for PIR motion sensor
                PinPIR = gpio.OpenPin(PIR_PIN);

                //PIR motion sensor - Ignore changes in value of less than 50ms
                PinPIR.DebounceTimeout = new TimeSpan(0, 0, 0, 0, 50);

                //set the mode as Input (we are READING a signal from this port)
                PinPIR.SetDriveMode(GpioPinDriveMode.Input);

                //wire the ValueChanged event to the PinPIR_ValueChanged() function
                //when this value changes (motion is detected), the function is called
                PinPIR.ValueChanged += PinPIR_ValueChanged;

                GpioStatus.Text = "GPIO pins " + LED_PIN.ToString() + " & " + PIR_PIN.ToString() + " initialized correctly.";
            }
            catch (Exception ex)
            {
                GpioStatus.Text = "GPIO init error: " + ex.Message;
            }

        }

        /// <summary>
        /// Event called when GPIO PIN 16 changes (PIR signal)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void PinPIR_ValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            //simple guard to prevent it from triggering this function again before it's compelted the first time - one photo at a time please
            if (IsInPictureCaptureMode)
                return;
            else
                IsInPictureCaptureMode = true;

            //turn off the LED because we're about to take a picture and upload it
            LightLED(false);

            try
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                async () =>
                {
                    PIRStatus.Text = "New PIR pin value: " + args.Edge.ToString();
                    StorageFile picture = await TakePicture();

                    if (picture != null)
                        await UploadPictureToAzure(picture);
                });
            }
            catch (Exception ex)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    PIRStatus.Text = "PIR Error: " + ex.Message;
                });
            }
            finally
            {
                //reset the "IsInPictureMode" singleton guard so the next 
                //PIR movement can come into this method and take a picture
                IsInPictureCaptureMode = false;

                //Turn the LED Status Light on - we're ready for another picture
                LightLED(true);
            }

            return;
        }

        /// <summary>
        /// Toggles LOW/HIGH values to LED's GPIO port
        /// to turn LED either on or off
        /// </summary>
        private void LightLED(bool show = true)
        {
            if (PinLED == null)
                return;

            if (show)
            {
                PinLED.Write(GpioPinValue.Low);
            }
            else
            {
                PinLED.Write(GpioPinValue.High);
            }
        }
        #endregion

        #region Webcam code
        /// <summary>
        /// Initializes the USB Webcam
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void InitilizeWebcam(object sender = null, RoutedEventArgs e = null)
        {
            try
            {
                //initialize the WebCam via MediaCapture object
                MediaCap = new MediaCapture();
                await MediaCap.InitializeAsync();

                // Set callbacks for any possible failure in TakePicture() logic
                MediaCap.Failed += new MediaCaptureFailedEventHandler(MediaCapture_Failed);

                AppStatus.Text = "Camera initialized...Waiting for MOTION";
            }
            catch (Exception ex)
            {
                AppStatus.Text = "Unable to initialize camera for audio/video mode: " + ex.Message;
            }

            return;
        }

        /// <summary>
        /// Takes a picture from the webcam
        /// </summary>
        /// <returns>StorageFile of image</returns>
        public async Task<StorageFile> TakePicture()
        {
            try
            {
                //captureImage is our Xaml image control (to preview the picture onscreen)
                CaptureImage.Source = null;

                //gets a reference to the file we're about to write a picture into
                StorageFile photoFile = await KnownFolders.PicturesLibrary.CreateFileAsync(
                    "RaspPiSecurityPic.jpg", CreationCollisionOption.GenerateUniqueName);

                //use the MediaCapture object to stream captured photo to a file
                ImageEncodingProperties imageProperties = ImageEncodingProperties.CreateJpeg();
                await MediaCap.CapturePhotoToStorageFileAsync(imageProperties, photoFile);

                //show photo onscreen
                IRandomAccessStream photoStream = await photoFile.OpenReadAsync();
                BitmapImage bitmap = new BitmapImage();
                bitmap.SetSource(photoStream);
                CaptureImage.Source = bitmap;

                AppStatus.Text = "Took Photo: " + photoFile.Name;

                return photoFile;
            }
            catch (Exception ex)
            {
                //write the exception on screen
                AppStatus.Text = "Error taking picture: " + ex.Message;

                return null;
            }
        }

        /// <summary>
        /// Callback function for any failures in MediaCapture operations
        /// </summary>
        /// <param name="currentCaptureObject"></param>
        /// <param name="currentFailure"></param>
        private void MediaCapture_Failed(MediaCapture currentCaptureObject, MediaCaptureFailedEventArgs currentFailure)
        {
            AppStatus.Text = currentFailure.Message;
        }
        #endregion

        #region Azure Code
        /// <summary>
        /// Upload the StorageFile to Azure Blob Storage
        /// </summary>
        /// <param name="file">The StorageFile to upload</param>
        /// <returns>null</returns>
        private async Task UploadPictureToAzure(StorageFile file)
        {
            try
            {
                StorageCredentials creds = new StorageCredentials(Azure_StorageAccountName, Azure_AccessKey);
                CloudStorageAccount account = new CloudStorageAccount(creds, useHttps: true);
                CloudBlobClient client = account.CreateCloudBlobClient();
                CloudBlobContainer sampleContainer = client.GetContainerReference(Azure_ContainerName);

                CloudBlockBlob blob = sampleContainer.GetBlockBlobReference(file.Name);

                await blob.UploadFromFileAsync(file);
            }
            catch (Exception ex)
            {
                AppStatus.Text = ex.ToString();
            }
        }

        #endregion
    }
}
