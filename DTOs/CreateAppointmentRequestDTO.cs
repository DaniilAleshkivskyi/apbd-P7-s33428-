using System.ComponentModel.DataAnnotations;

namespace APBD_TASK6.DTOs;

public class CreateAppointmentRequestDTO
{
    [Required]
    public string Title { get; set; } = string.Empty;
    [Required]
    public string Description { get; set; } = string.Empty;
    [Required]
}