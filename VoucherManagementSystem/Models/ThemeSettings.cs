using System.ComponentModel.DataAnnotations;

namespace VoucherManagementSystem.Models
{
    public class ThemeSettings
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string ThemeMode { get; set; } = "Light"; // Light, Dark, SemiDark

        [Required]
        [MaxLength(7)]
        public string PrimaryColor { get; set; } = "#0d6efd"; // Bootstrap primary blue

        [Required]
        [MaxLength(7)]
        public string SecondaryColor { get; set; } = "#6c757d"; // Bootstrap secondary

        [Required]
        [MaxLength(7)]
        public string SuccessColor { get; set; } = "#198754"; // Bootstrap success

        [Required]
        [MaxLength(7)]
        public string DangerColor { get; set; } = "#dc3545"; // Bootstrap danger

        [Required]
        [MaxLength(7)]
        public string WarningColor { get; set; } = "#ffc107"; // Bootstrap warning

        [Required]
        [MaxLength(7)]
        public string InfoColor { get; set; } = "#0dcaf0"; // Bootstrap info

        [Required]
        [MaxLength(7)]
        public string BackgroundColor { get; set; } = "#ffffff"; // White background

        [Required]
        [MaxLength(7)]
        public string TextColor { get; set; } = "#212529"; // Dark text

        [Required]
        [MaxLength(7)]
        public string CardBackgroundColor { get; set; } = "#ffffff"; // Card background

        [Required]
        [MaxLength(7)]
        public string NavbarBackgroundColor { get; set; } = "#ffffff"; // Navbar background

        [Required]
        [MaxLength(7)]
        public string SidebarBackgroundColor { get; set; } = "#ffffff"; // Sidebar background

        [Required]
        [MaxLength(7)]
        public string FooterBackgroundColor { get; set; } = "#f8f9fa"; // Footer background

        public DateTime LastUpdated { get; set; } = DateTime.Now;

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
