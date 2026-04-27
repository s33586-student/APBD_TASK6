using APBD_TASK6.Dtos;
using APBD_TASK6.Interfaces;
using APBD_TASK6.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;
        private IAppointmentService _appointmentService;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException();
            _appointmentService = new AppointmentService(_connectionString);
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

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

            await connection.OpenAsync();

            var results = new List<AppointmentListDto>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new AppointmentListDto
                {
                    IdAppointment = reader.GetInt32(0),
                    AppointmentDate = reader.GetDateTime(1),
                    Status = reader.GetString(2),
                    Reason = reader.GetString(3),
                    PatientFullName = reader.GetString(4),
                    PatientEmail = reader.GetString(5)
                });
            }

            return Ok(results);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAppointmentById(int id)
        {
            const string sql = """
                SELECT
                    a.IdAppointment,
                    a.AppointmentDate,
                    a.Status,
                    a.Reason,
                    p.FirstName + N' ' + p.LastName AS PatientFullName,
                    p.Email AS PatientEmail,
                    p.PhoneNumber as PatientPhoneNumber,
                    d.LicenseNumber as DoctorLicenseNumber,
                    a.InternalNotes,
                    a.CreatedAt as CreationDate
                FROM dbo.Appointments a
                JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
                WHERE (@Id IS NULL OR a.IdAppointment = @Id)
                ORDER BY a.AppointmentDate;
                """;
                

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@Id", id);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var result = new AppointmentDetailsDto
                {
                    IdAppointment = reader.GetInt32(0),
                    AppointmentDate = reader.GetDateTime(1),
                    Status = reader.GetString(2),
                    Reason = reader.GetString(3),
                    PatientFullName = reader.GetString(4),
                    PatientEmail = reader.GetString(5),
                    PatientPhoneNumber = reader.GetString(6),
                    DoctorLicenseNumber = reader.GetString(7),
                    InternalNotes = reader.GetString(8),
                    CreationDate = reader.GetDateTime(9)
                };
                return Ok(result);
            }
            
            return NotFound(new ErrorResponseDto("Appointment with this Id cannot be found."));
        }


        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
        {

            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
            {
                return BadRequest(new ErrorResponseDto("Reason cannot be empty and should have at most 250 characters."));
            }

            if (request.AppointmentDate < DateTime.UtcNow)
            {
                return BadRequest(new ErrorResponseDto("Appointment date cannot be in the past."));
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var (dp_ok, dp_error) = await _appointmentService
                .ValidateDoctorAndPatient(request.IdDoctor, request.IdPatient);
            if (!dp_ok)
                return dp_error!;

            var (date_ok, date_error) = await _appointmentService
                .ValidateDoctorDateConflict(request.IdDoctor, request.AppointmentDate);
            if (!date_ok)
                return date_error!;


            int newId;

            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                const string insertSql = """
                    INSERT INTO dbo.Appointments (IdDoctor, IdPatient, AppointmentDate, Reason, Status)
                    OUTPUT INSERTED.IdAppointment
                    VALUES (@IdDoctor, @IdPatient, @AppointmentDate, @Reason, 'Scheduled');
                    """;

                await using var command = new SqlCommand(insertSql, connection, transaction);
                command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                command.Parameters.AddWithValue("@Reason", request.Reason);

                newId = (int)(await command.ExecuteScalarAsync())!;
                await transaction.CommitAsync();
            }

            return CreatedAtRoute(nameof(GetAppointments), new { id = newId }, null);
        }
    
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAppointment(int id, [FromBody] UpdateAppointmentRequestDto request)
        {
            if (request.Status is not ("Scheduled" or "Completed" or "Cancelled"))
                return BadRequest(new ErrorResponseDto("Status must be one of: Scheduled, Completed, Cancelled."));

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();


            var (dp_ok, dp_error) = await _appointmentService
                .ValidateDoctorAndPatient(request.IdDoctor, request.IdPatient);
            if (!dp_ok)
                return dp_error!;

            var (date_ok, date_error) = await _appointmentService
                .ValidateDoctorDateWithAppointmentId(request.IdDoctor, request.AppointmentDate, id);
            if (!date_ok)
                return date_error!;

            var (status_ok, status_error) = await _appointmentService
                .ValidateAppointmentCompletionDateChange(id, request.AppointmentDate);
            if (!status_ok)
                return status_error!;
            

            const string updateSql = """
            UPDATE dbo.Appointments
            SET
                IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """;

            await using var commandUpdate = new SqlCommand(updateSql, connection);

            commandUpdate.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            commandUpdate.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            commandUpdate.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            commandUpdate.Parameters.AddWithValue("@Status", request.Status);
            commandUpdate.Parameters.AddWithValue("@Reason", request.Reason);
            commandUpdate.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
            commandUpdate.Parameters.AddWithValue("@IdAppointment", id);

            await commandUpdate.ExecuteNonQueryAsync();

            return Ok();
        }


        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAppointment(int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var (status_ok, status_error) = await _appointmentService
                .ValidateAppointmentCompletion(id);
            if (!status_ok)
                return status_error!;
            

            const string delSql = """
            DELETE FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """;

            await using var commandDelete = new SqlCommand(delSql, connection);

            commandDelete.Parameters.AddWithValue("@IdAppointment", id);

            await commandDelete.ExecuteNonQueryAsync();

            return NoContent();
        }


    }
}