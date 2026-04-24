using System.ComponentModel.DataAnnotations;

namespace APBD_TASK6.DTOs;

public class CreateAppointmentRequestDTO
{
    [Required]
    public int IdDoctor { get; set; }
    [Required]
    public int IdPatient { get; set; }
    [Required]
    public DateTime AppointmentDate { get; set; }
    [Required]  
    [StringLength(250, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}