using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Missing 'DefaultConnection' in appsettings.json.");
        }


        [HttpGet]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? patientLastName)
        {
            const string sql = """
                               SELECT
                                   a.IdAppointment,
                                   a.AppointmentDate,
                                   a.Status,
                                   a.Reason,
                                   p.FirstName + N' ' + p.LastName AS PatientFullName,
                                   p.Email AS PatientEmail
                               FROM dbo.Appointments a
                               JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                               WHERE (@Status IS NULL OR a.Status = @Status)
                                 AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                               ORDER BY a.AppointmentDate;
                               """;
            
            await using var connection = new SqlConnection( _connectionString);
            await using var command = new SqlCommand(sql, connection);
            
            
            command.Parameters.AddWithValue("@Status",(object?) status ?? DBNull.Value);
            command.Parameters.AddWithValue("@PatientLastName", (object?) patientLastName ?? DBNull.Value);
            
            await connection.OpenAsync();
            var results = new List<AppointmentListDto>();
            
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new AppointmentListDto
                {
                    IdAppointment =  (int)reader["IdAppointment"],
                    AppointmentDate = (DateTime)reader["AppointmentDate"],
                    Status = (string)reader["Status"],
                    Reason = (string)reader["Reason"],
                    PatientFullName = (string)reader["PatientFullName"],
                    PatientEmail = (string)reader["PatientEmail"]
                });
            }
            return Ok(results);
        }
        
        
        [HttpGet("{idAppointment:int}")]
        public async Task<IActionResult> GetAppointmentById(int idAppointment)
        {
            const string sql = """
                               SELECT
                                   a.IdAppointment,
                                   a.AppointmentDate,
                                   a.Status,
                                   a.Reason,
                                   a.InternalNotes,
                                   a.CreatedAt,
                                   p.FirstName + N' ' + p.LastName AS PatientFullName,
                                   p.Email AS PatientEmail,
                                   p.PhoneNumber AS PatientPhoneNumber,
                                   d.FirstName + N' ' + d.LastName AS DoctorFullName,
                                   d.LicenseNumber AS DoctorLicenseNumber
                               FROM dbo.Appointments a
                               JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                               JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
                               WHERE a.IdAppointment = @IdAppointment;
                               """;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound(new ErrorResponceDTO { Message = $"Appointment {idAppointment} not found" });

            var dto = new AppointmentDetailsDto
            {
                IdAppointment = (int)reader["IdAppointment"],
                AppointmentDate = (DateTime)reader["AppointmentDate"],
                Status = (string)reader["Status"],
                Reason = (string)reader["Reason"],
                InternalNotes = reader["InternalNotes"] as string,
                CreatedAt = (DateTime)reader["CreatedAt"],
                PatientFullName = (string)reader["PatientFullName"],
                PatientEmail = (string)reader["PatientEmail"],
                PatientPhoneNumber = (string)reader["PatientPhoneNumber"],
                DoctorFullName = (string)reader["DoctorFullName"],
                DoctorLicenseNumber = (string)reader["DoctorLicenseNumber"]
            };

            return Ok(dto);
        }
        
        
        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDTO request)
        {
            if (request.AppointmentDate < DateTime.UtcNow)
                return BadRequest(new ErrorResponceDTO { Message = "Appointment date cannot be in the past" });
            
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            await using (var cmd = new SqlCommand("SELECT IsActive FROM dbo.Patients WHERE IdPatient = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", request.IdPatient);
                var result = await cmd.ExecuteScalarAsync();
                if (result is null) return NotFound(new ErrorResponceDTO { Message = "Patient not found" });
            }
            
            await using (var cmd = new SqlCommand("SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", request.IdDoctor);
                var result = await cmd.ExecuteScalarAsync();
                if (result is null) return NotFound(new ErrorResponceDTO { Message = "Doctor not found" });
            }
            
            await using (var cmd = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                  AND AppointmentDate = @AppointmentDate
                  AND Status = 'Scheduled';
                """, connection))
            {
                cmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                cmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                var count = (int)(await cmd.ExecuteScalarAsync())!;
                if (count > 0) return Conflict(new ErrorResponceDTO { Message = "Doctor already has an appointment at this time" });
            }
            
            int newId;
            await using (var cmd = new SqlCommand("""
                INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Reason, Status)
                OUTPUT INSERTED.IdAppointment
                VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Reason, 'Scheduled');
                """, connection))
            {
                cmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                cmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                cmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                cmd.Parameters.AddWithValue("@Reason", request.Reason);
                newId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            return CreatedAtAction(nameof(GetAppointmentById), new { idAppointment = newId }, new { idAppointment = newId });
        }
        [HttpPut("{idAppointment:int}")]
        public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
        {
            var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
            if (!validStatuses.Contains(request.Status))
                return BadRequest(new ErrorResponceDTO { Message = "Invalid status" });

            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new ErrorResponceDTO { Message = "Reason is required" });

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            
            string currentStatus;
            DateTime currentDate;
            await using (var cmd = new SqlCommand("SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", idAppointment);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return NotFound(new ErrorResponceDTO { Message = $"Appointment {idAppointment} not found" });
                currentStatus = (string)reader["Status"];
                currentDate = (DateTime)reader["AppointmentDate"];
            }


            if (currentStatus == "Completed" && request.AppointmentDate != currentDate)
                return BadRequest(new ErrorResponceDTO { Message = "Cannot change date of a completed appointment" });


            
            await using (var cmd = new SqlCommand("SELECT IsActive FROM dbo.Patients WHERE IdPatient = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", request.IdPatient);
                var result = await cmd.ExecuteScalarAsync();
                if (result is null) return NotFound(new ErrorResponceDTO { Message = "Patient not found" });
            }
            
            await using (var cmd = new SqlCommand("SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", request.IdDoctor);
                var result = await cmd.ExecuteScalarAsync();
                if (result is null) return NotFound(new ErrorResponceDTO { Message = "Doctor not found" });
            }
            
            await using (var cmd = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                  AND AppointmentDate = @AppointmentDate
                  AND Status = 'Scheduled'
                  AND IdAppointment <> @IdAppointment;
                """, connection))
            {
                cmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                cmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                cmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
                var count = (int)(await cmd.ExecuteScalarAsync())!;
                if (count > 0) return Conflict(new ErrorResponceDTO { Message = "Doctor already has an appointment at this time" });
            }
            
            await using (var cmd = new SqlCommand("""
                UPDATE dbo.Appointments
                SET IdPatient = @IdPatient,
                    IdDoctor = @IdDoctor,
                    AppointmentDate = @AppointmentDate,
                    Status = @Status,
                    Reason = @Reason,
                    InternalNotes = @InternalNotes
                WHERE IdAppointment = @IdAppointment;
                """, connection))
            {
                cmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                cmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                cmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                cmd.Parameters.AddWithValue("@Reason", request.Reason);
                cmd.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
                await cmd.ExecuteNonQueryAsync();
            }

            return Ok();
        }
        
        [HttpDelete("{idAppointment:int}")]
        public async Task<IActionResult> DeleteAppointment(int idAppointment)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            string currentStatus;
            await using (var cmd = new SqlCommand("SELECT Status FROM dbo.Appointments WHERE IdAppointment = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", idAppointment);
                var result = await cmd.ExecuteScalarAsync();
                if (result is null)
                    return NotFound(new ErrorResponceDTO { Message = $"Appointment {idAppointment} not found" });
                currentStatus = (string)result;
            }
            
            if (currentStatus == "Completed")
                return Conflict(new ErrorResponceDTO { Message = "Cannot delete a completed appointment" });
            
            await using (var cmd = new SqlCommand("DELETE FROM dbo.Appointments WHERE IdAppointment = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", idAppointment);
                await cmd.ExecuteNonQueryAsync();
            }

            return NoContent();
        }
    }
}
