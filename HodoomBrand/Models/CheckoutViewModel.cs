using System.ComponentModel.DataAnnotations;

namespace HodoomBrand.Models;

public class CheckoutViewModel
{
    [Required, StringLength(100)]
    [Display(Name = "Full name")]
    public string CustomerName { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [RegularExpression(@"^01[0125][0-9]{8}$", ErrorMessage = "Enter a valid Egyptian mobile number (e.g. 01012345678).")]
    public string Phone { get; set; } = "";

    [Required]
    public string Governorate { get; set; } = "";

    [Required, StringLength(300)]
    [Display(Name = "Full address")]
    public string Address { get; set; } = "";

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool PayOnline { get; set; }
}