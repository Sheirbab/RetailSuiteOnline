using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ItechMicrosystems.Web.Pages;

public class ContactModel : PageModel
{
    private readonly ILogger<ContactModel> _logger;

    public ContactModel(ILogger<ContactModel> logger)
    {
        _logger = logger;
    }

    [BindProperty]
    public ContactForm Form { get; set; } = new();

    public bool Submitted { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
            return Page();

        // TODO: this only logs the enquiry — no email is actually sent yet.
        // Wire this up to a real mail sender (SMTP relay or a service like
        // SendGrid) before relying on this form to reach customers.
        _logger.LogInformation(
            "Contact form submission from {Name} <{Email}>: {Message}",
            Form.Name, Form.Email, Form.Message);

        Submitted = true;
        ModelState.Clear();
        Form = new ContactForm();
        return Page();
    }

    public class ContactForm
    {
        [Required(ErrorMessage = "Please enter your name.")]
        [StringLength(120)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [Phone(ErrorMessage = "Please enter a valid phone number.")]
        [StringLength(40)]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Please enter a message.")]
        [StringLength(2000)]
        public string Message { get; set; } = "";
    }
}
