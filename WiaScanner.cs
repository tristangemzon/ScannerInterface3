using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace ScannerInterface3
{
    /// <summary>
    /// WIA (Windows Image Acquisition) scanner implementation.
    /// Used as a fallback when TWAIN scanning fails.
    /// </summary>
    public class WiaScanner
    {
        private string logFile = "";

        // WIA Constants - Property IDs
        private const string WIA_DEVICE_PROPERTY_DOCUMENT_HANDLING_SELECT = "3088";
        private const string WIA_DEVICE_PROPERTY_DOCUMENT_HANDLING_STATUS = "3087";
        private const string WIA_DEVICE_PROPERTY_PAGES = "3096";
        private const string WIA_ITEM_PROPERTY_COLOR_MODE = "6146";
        private const string WIA_ITEM_PROPERTY_HORIZONTAL_RESOLUTION = "6147";
        private const string WIA_ITEM_PROPERTY_VERTICAL_RESOLUTION = "6148";
        private const string WIA_ITEM_PROPERTY_HORIZONTAL_EXTENT = "6151";
        private const string WIA_ITEM_PROPERTY_VERTICAL_EXTENT = "6152";
        private const string WIA_ITEM_PROPERTY_FORMAT = "4106";

        // WIA Format GUIDs
        private const string WIA_FORMAT_BMP = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
        private const string WIA_FORMAT_PNG = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
        private const string WIA_FORMAT_JPEG = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
        private const string WIA_FORMAT_TIFF = "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}";

        // Document handling flags
        private const int FEEDER = 1;
        private const int FLATBED = 2;
        private const int DUPLEX = 4;
        private const int FRONT_FIRST = 8;
        private const int BACK_FIRST = 16;
        private const int FRONT_ONLY = 32;
        private const int BACK_ONLY = 64;
        private const int NEXT_PAGE = 128;
        private const int PREFEED = 256;
        private const int AUTO_ADVANCE = 512;

        // Color modes
        private const int COLOR_MODE_BW = 0;       // Black and White (1-bit)
        private const int COLOR_MODE_GRAY = 2;     // Grayscale
        private const int COLOR_MODE_COLOR = 1;   // Color

        /// <summary>
        /// Scan documents to PDF using WIA.
        /// </summary>
        public string ScanToPdf(int deviceIndex, string outputPdfPath,
            bool useFeeder, bool useDuplex,
            string colorMode, string resolution,
            int pageWidth, int pageHeight)
        {
            logFile = Path.ChangeExtension(outputPdfPath, ".log");
            Log("WIA fallback: Starting WIA scan...");

            try
            {
                // Create WIA DeviceManager
                var deviceManager = CreateDeviceManager();
                if (deviceManager == null)
                {
                    return "Failed to create WIA DeviceManager.";
                }

                // Get device info
                dynamic deviceInfos = deviceManager.DeviceInfos;
                int scannerCount = 0;
                dynamic targetDeviceInfo = null;
                int currentIndex = 0;

                foreach (dynamic deviceInfo in deviceInfos)
                {
                    // Type 1 = Scanner
                    if ((int)deviceInfo.Type == 1)
                    {
                        if (currentIndex == deviceIndex)
                        {
                            targetDeviceInfo = deviceInfo;
                            break;
                        }
                        currentIndex++;
                    }
                    scannerCount++;
                }

                if (targetDeviceInfo == null)
                {
                    return $"WIA: No scanner found at index {deviceIndex}.";
                }

                string deviceName = targetDeviceInfo.Properties["Name"].Value.ToString();
                Log($"WIA: Connecting to device: {deviceName}");

                // Connect to the device
                dynamic device = targetDeviceInfo.Connect();
                if (device == null)
                {
                    return "WIA: Failed to connect to scanner.";
                }

                var pages = new List<Bitmap>();

                try
                {
                    // Configure device for feeder/duplex if requested
                    if (useFeeder || useDuplex)
                    {
                        ConfigureDocumentHandling(device, useFeeder, useDuplex);
                    }

                    // Get scan item - log available items for diagnostics
                    dynamic items = device.Items;
                    Log($"WIA: Device has {items.Count} item(s).");
                    
                    if (items.Count == 0)
                    {
                        return "WIA: No scanner items available.";
                    }

                    // Try to find the appropriate item (some scanners have separate flatbed/feeder items)
                    dynamic scanItem = items[1]; // Default to first item (WIA uses 1-based indexing)
                    
                    // Log item info for debugging
                    try
                    {
                        for (int i = 1; i <= items.Count; i++)
                        {
                            dynamic item = items[i];
                            string itemName = "Unknown";
                            try { itemName = item.Properties["Item Name"].Value.ToString(); } catch { }
                            Log($"WIA: Item {i}: {itemName}");
                        }
                    }
                    catch { }

                    // Set scan properties - each wrapped individually as some scanners don't support all
                    int dpi = GetDpi(resolution);
                    int wiaColorMode = GetWiaColorMode(colorMode);

                    try
                    {
                        SetItemProperty(scanItem, WIA_ITEM_PROPERTY_HORIZONTAL_RESOLUTION, dpi);
                        Log($"WIA: Set horizontal resolution to {dpi}");
                    }
                    catch (Exception ex) { Log($"WIA: Could not set horizontal resolution: {ex.Message}"); }

                    try
                    {
                        SetItemProperty(scanItem, WIA_ITEM_PROPERTY_VERTICAL_RESOLUTION, dpi);
                        Log($"WIA: Set vertical resolution to {dpi}");
                    }
                    catch (Exception ex) { Log($"WIA: Could not set vertical resolution: {ex.Message}"); }

                    try
                    {
                        SetItemProperty(scanItem, WIA_ITEM_PROPERTY_COLOR_MODE, wiaColorMode);
                        Log($"WIA: Set color mode to {wiaColorMode}");
                    }
                    catch (Exception ex) { Log($"WIA: Could not set color mode: {ex.Message}"); }

                    // Try to set extent (optional - some scanners don't support custom extents)
                    try
                    {
                        if (pageWidth > 0 && pageHeight > 0)
                        {
                            // Calculate extent based on resolution and page size (page size in 1/1000 inch)
                            int horizontalExtent = (int)((pageWidth / 1000.0) * dpi);
                            int verticalExtent = (int)((pageHeight / 1000.0) * dpi);
                            SetItemProperty(scanItem, WIA_ITEM_PROPERTY_HORIZONTAL_EXTENT, horizontalExtent);
                            SetItemProperty(scanItem, WIA_ITEM_PROPERTY_VERTICAL_EXTENT, verticalExtent);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"WIA: Could not set scan extent (using default): {ex.Message}");
                    }

                    // Scan pages
                    bool hasMorePages = true;
                    int pageCount = 0;
                    const int maxPages = 100; // Safety limit

                    // Try to set output format to BMP (most compatible)
                    try
                    {
                        SetItemProperty(scanItem, WIA_ITEM_PROPERTY_FORMAT, WIA_FORMAT_BMP);
                    }
                    catch
                    {
                        Log("WIA: Could not set format property, using device default.");
                    }

                    while (hasMorePages && pageCount < maxPages)
                    {
                        try
                        {
                            Log($"WIA: Scanning page {pageCount + 1}...");

                            // Try multiple transfer methods
                            dynamic imageFile = null;
                            Exception lastError = null;
                            
                            // Method 1: Try Transfer() with format GUID string
                            try
                            {
                                Log("WIA: Trying Transfer with BMP format GUID...");
                                imageFile = scanItem.Transfer(WIA_FORMAT_BMP);
                            }
                            catch (Exception ex1)
                            {
                                lastError = ex1;
                                Log($"WIA: Transfer(format) failed: {ex1.Message}");
                                
                                // Method 2: Try parameterless Transfer()
                                try
                                {
                                    Log("WIA: Trying parameterless Transfer...");
                                    imageFile = scanItem.Transfer();
                                }
                                catch (Exception ex2)
                                {
                                    lastError = ex2;
                                    Log($"WIA: Transfer() failed: {ex2.Message}");
                                    
                                    // Method 3: Use CommonDialog for transfer
                                    try
                                    {
                                        Log("WIA: Trying CommonDialog.ShowTransfer...");
                                        Type commonDialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
                                        dynamic commonDialog = Activator.CreateInstance(commonDialogType);
                                        imageFile = commonDialog.ShowTransfer(scanItem, WIA_FORMAT_BMP, false);
                                        Marshal.ReleaseComObject(commonDialog);
                                    }
                                    catch (Exception ex3)
                                    {
                                        lastError = ex3;
                                        Log($"WIA: ShowTransfer failed: {ex3.Message}");
                                    }
                                }
                            }
                            
                            if (imageFile == null)
                            {
                                string errMsg = lastError?.Message ?? "Unknown error";
                                if (lastError is COMException comEx)
                                {
                                    errMsg = $"0x{(uint)comEx.ErrorCode:X8}: {comEx.Message}";
                                }
                                throw new Exception($"All transfer methods failed. Last error: {errMsg}");
                            }

                            // Get the image data from WIA ImageFile
                            dynamic vector = imageFile.FileData;
                            byte[] imageData = (byte[])vector.BinaryData;
                            
                            Log($"WIA: Received {imageData.Length} bytes of image data.");

                            using (var ms = new MemoryStream(imageData))
                            {
                                var bitmap = new Bitmap(ms);
                                pages.Add(new Bitmap(bitmap)); // Clone the bitmap
                            }
                            pageCount++;
                            Log($"WIA: Page {pageCount} scanned successfully.");

                            // Release COM objects
                            Marshal.ReleaseComObject(vector);
                            Marshal.ReleaseComObject(imageFile);

                            // Check if there are more pages in feeder
                            if (useFeeder)
                            {
                                hasMorePages = HasMorePagesInFeeder(device);
                                if (hasMorePages)
                                {
                                    // For ADF, we need to get a fresh scan item for each page
                                    try
                                    {
                                        items = device.Items;
                                        if (items.Count > 0)
                                        {
                                            scanItem = items[1];
                                            // Re-apply format setting for next page
                                            try
                                            {
                                                SetItemProperty(scanItem, WIA_ITEM_PROPERTY_FORMAT, WIA_FORMAT_BMP);
                                            }
                                            catch { }
                                        }
                                        else
                                        {
                                            hasMorePages = false;
                                        }
                                    }
                                    catch
                                    {
                                        hasMorePages = false;
                                    }
                                }
                            }
                            else
                            {
                                hasMorePages = false; // Flatbed only scans one page
                            }
                        }
                        catch (COMException comEx)
                        {
                            // WIA error codes:
                            // 0x80210003 = WIA_ERROR_PAPER_EMPTY - no more pages
                            // 0x80210006 = WIA_ERROR_ITEM_DELETED
                            // 0x80070057 = E_INVALIDARG - parameter incorrect
                            uint errorCode = (uint)comEx.ErrorCode;
                            if (errorCode == 0x80210003)
                            {
                                Log("WIA: No more pages in feeder.");
                                hasMorePages = false;
                            }
                            else if (errorCode == 0x80210006)
                            {
                                Log("WIA: Scanner item no longer available.");
                                hasMorePages = false;
                            }
                            else if (errorCode == 0x80070057 && pageCount > 0)
                            {
                                // Parameter incorrect after successful scans usually means no more pages
                                Log("WIA: No more pages (E_INVALIDARG after successful scan).");
                                hasMorePages = false;
                            }
                            else
                            {
                                Log($"WIA: COM error during scan: {comEx.Message} (0x{errorCode:X8})");
                                if (pageCount == 0)
                                {
                                    throw; // Re-throw if no pages were scanned
                                }
                                hasMorePages = false;
                            }
                        }
                    }
                }
                finally
                {
                    // Release device COM object
                    if (device != null)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                if (pages.Count == 0)
                {
                    return "WIA: No pages were scanned.";
                }

                // Create PDF from scanned pages
                Log($"WIA: Creating PDF with {pages.Count} page(s)...");
                CreatePdfFromBitmaps(pages, outputPdfPath);

                // Cleanup
                foreach (var page in pages)
                {
                    page.Dispose();
                }
                pages.Clear();

                Log("WIA: Scan completed successfully.");
                return ""; // Empty string indicates success
            }
            catch (COMException comEx)
            {
                string errorMsg = $"WIA COM error: {comEx.Message} (0x{comEx.ErrorCode:X8})";
                Log(errorMsg);
                return errorMsg;
            }
            catch (Exception ex)
            {
                string errorMsg = $"WIA error: {ex.Message}";
                Log(errorMsg);
                return errorMsg;
            }
        }

        /// <summary>
        /// Get list of available WIA scanner devices.
        /// </summary>
        public string GetAvailableScanners()
        {
            string scannerList = "";
            try
            {
                var deviceManager = CreateDeviceManager();
                if (deviceManager == null)
                {
                    return "";
                }

                dynamic deviceInfos = deviceManager.DeviceInfos;
                int index = 0;

                foreach (dynamic deviceInfo in deviceInfos)
                {
                    // Type 1 = Scanner
                    if ((int)deviceInfo.Type == 1)
                    {
                        string name = deviceInfo.Properties["Name"].Value.ToString();
                        scannerList += $"{index}={name} (WIA)\r\n";
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Swallow exceptions
            }
            return scannerList;
        }

        private dynamic CreateDeviceManager()
        {
            try
            {
                Type deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
                if (deviceManagerType == null)
                {
                    Log("WIA: DeviceManager type not found. WIA may not be installed.");
                    return null;
                }
                return Activator.CreateInstance(deviceManagerType);
            }
            catch (Exception ex)
            {
                Log($"WIA: Failed to create DeviceManager: {ex.Message}");
                return null;
            }
        }

        private void ConfigureDocumentHandling(dynamic device, bool useFeeder, bool useDuplex)
        {
            try
            {
                int handlingFlag = 0;

                if (useFeeder)
                {
                    handlingFlag |= FEEDER;
                }
                else
                {
                    handlingFlag |= FLATBED;
                }

                if (useDuplex)
                {
                    handlingFlag |= DUPLEX;
                }

                SetDeviceProperty(device, WIA_DEVICE_PROPERTY_DOCUMENT_HANDLING_SELECT, handlingFlag);

                // Set pages to 0 = scan all pages
                SetDeviceProperty(device, WIA_DEVICE_PROPERTY_PAGES, 0);
            }
            catch (Exception ex)
            {
                Log($"WIA: Warning - Could not set document handling: {ex.Message}");
            }
        }

        private bool HasMorePagesInFeeder(dynamic device)
        {
            try
            {
                foreach (dynamic prop in device.Properties)
                {
                    if (prop.PropertyID.ToString() == WIA_DEVICE_PROPERTY_DOCUMENT_HANDLING_STATUS)
                    {
                        int status = (int)prop.Value;
                        return (status & FEEDER) != 0;
                    }
                }
            }
            catch
            {
                // Ignore errors checking feeder status
            }
            return false;
        }

        private void SetDeviceProperty(dynamic device, string propertyId, object value)
        {
            try
            {
                foreach (dynamic prop in device.Properties)
                {
                    if (prop.PropertyID.ToString() == propertyId)
                    {
                        prop.Value = value;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"WIA: Could not set device property {propertyId}: {ex.Message}");
            }
        }

        private void SetItemProperty(dynamic item, string propertyId, object value)
        {
            try
            {
                foreach (dynamic prop in item.Properties)
                {
                    if (prop.PropertyID.ToString() == propertyId)
                    {
                        prop.Value = value;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"WIA: Could not set item property {propertyId}: {ex.Message}");
            }
        }

        private int GetDpi(string resolution)
        {
            switch (resolution?.ToLower())
            {
                case "low":
                    return 100;
                case "medium":
                    return 200;
                case "high":
                    return 300;
                default:
                    return 200;
            }
        }

        private int GetWiaColorMode(string colorMode)
        {
            switch (colorMode?.ToLower())
            {
                case "bw":
                    return COLOR_MODE_BW;
                case "gray":
                    return COLOR_MODE_GRAY;
                case "color":
                    return COLOR_MODE_COLOR;
                default:
                    return COLOR_MODE_COLOR;
            }
        }

        private void CreatePdfFromBitmaps(List<Bitmap> pages, string outputPath)
        {
            using (PdfDocument pdf = new PdfDocument())
            {
                pdf.Info.Title = "Scanned Document";

                foreach (var page in pages)
                {
                    if (page == null || page.Size == Size.Empty)
                        continue;

                    var pdfPage = pdf.AddPage();
                    pdfPage.Size = PdfSharp.PageSize.Letter;
                    pdfPage.Orientation = PdfSharp.PageOrientation.Portrait;

                    using (XGraphics gfx = XGraphics.FromPdfPage(pdfPage))
                    {
                        using (var ms = new MemoryStream())
                        {
                            page.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                            ms.Position = 0;

                            XImage img = XImage.FromStream(ms);

                            double xScale = pdfPage.Width / img.Width;
                            double yScale = pdfPage.Height / img.Height;
                            double scale = Math.Min(xScale, yScale);

                            double x = (pdfPage.Width - img.Width * scale) / 2;
                            double y = (pdfPage.Height - img.Height * scale) / 2;

                            gfx.DrawImage(img, x, y, img.PixelWidth * scale, img.PixelHeight * scale);
                        }
                    }
                }

                pdf.Save(outputPath);
            }
        }

        private void Log(string message)
        {
            if (!string.IsNullOrEmpty(logFile))
            {
                Helpers.Log(logFile, message);
            }
        }
    }
}
