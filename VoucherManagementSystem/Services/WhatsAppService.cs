using System.Diagnostics;
using VoucherManagementSystem.Models;

namespace VoucherManagementSystem.Services
{
    public class WhatsAppService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<WhatsAppService> _logger;

        public WhatsAppService(IWebHostEnvironment environment, ILogger<WhatsAppService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// Saves PDF to WhatsAppData folder and opens WhatsApp Web with pre-filled message
        /// </summary>
        public string SavePdfAndGetWhatsAppUrl(byte[] pdfData, string fileName, Customer customer, DateTime fromDate, DateTime toDate, decimal closingBalance, string balanceType)
        {
            try
            {
                // 1. Create WhatsAppData folder if it doesn't exist
                string whatsAppFolder = Path.Combine(_environment.ContentRootPath, "WhatsAppData");
                Directory.CreateDirectory(whatsAppFolder);

                // 2. Create timestamped filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeFileName = $"{timestamp}_{fileName}";
                string filePath = Path.Combine(whatsAppFolder, safeFileName);

                // 3. Save PDF file
                File.WriteAllBytes(filePath, pdfData);

                _logger.LogInformation($"PDF saved to: {filePath}");

                // 4. Prepare WhatsApp message
                string message = $"*Customer Ledger Report*\n" +
                                $"━━━━━━━━━━━━━━━━━━━━\n" +
                                $"📋 Customer: {customer.Name}\n" +
                                $"📅 Period: {fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}\n" +
                                $"💰 Closing Balance: Rs. {closingBalance:N0} {balanceType}\n\n" +
                                $"Please find the attached ledger report PDF.";

                // 5. Format phone number (remove spaces, dashes, etc.)
                string phoneNumber = FormatPhoneNumber(customer.Phone);

                // 6. Build WhatsApp Web URL
                string whatsappUrl;
                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    whatsappUrl = $"https://web.whatsapp.com/send?phone={phoneNumber}&text={Uri.EscapeDataString(message)}";
                }
                else
                {
                    whatsappUrl = $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(message)}";
                }

                return whatsappUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SavePdfAndGetWhatsAppUrl");
                throw;
            }
        }

        /// <summary>
        /// Formats phone number for WhatsApp (adds country code if needed)
        /// </summary>
        private string FormatPhoneNumber(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            // Remove all non-digit characters
            string phoneNumber = new string(phone.Where(char.IsDigit).ToArray());

            // If phone doesn't start with country code, add Pakistan code (+92)
            if (!phoneNumber.StartsWith("92"))
            {
                if (phoneNumber.StartsWith("0"))
                {
                    phoneNumber = "92" + phoneNumber.Substring(1);
                }
                else
                {
                    phoneNumber = "92" + phoneNumber;
                }
            }

            return phoneNumber;
        }

        /// <summary>
        /// Opens file explorer at the specified path
        /// </summary>
        public void OpenFileLocation(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening file location");
            }
        }

        /// <summary>
        /// Gets the most recently created file path in WhatsAppData folder
        /// </summary>
        public string? GetLatestFilePath()
        {
            try
            {
                string whatsAppFolder = Path.Combine(_environment.ContentRootPath, "WhatsAppData");

                if (!Directory.Exists(whatsAppFolder))
                    return null;

                var files = Directory.GetFiles(whatsAppFolder, "*.pdf")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                return files?.FullName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest file path");
                return null;
            }
        }
    }
}
