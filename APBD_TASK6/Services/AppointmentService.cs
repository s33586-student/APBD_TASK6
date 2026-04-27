using APBD_TASK6.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using APBD_TASK6.Interfaces;

namespace APBD_TASK6.Services
{
    public class AppointmentService : IAppointmentService
    {
        private readonly string _connectionString;

        public AppointmentService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<(bool ok, IActionResult? error)> ValidateDoctorAndPatient(int doctorId, int patientId)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            //// Doctor
            var doctorCmd = new SqlCommand(
                "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @Id",
                connection);

            doctorCmd.Parameters.AddWithValue("@Id", doctorId);

            var doctor = await doctorCmd.ExecuteScalarAsync();

            if (doctor == null)
                return (false, new NotFoundObjectResult(new ErrorResponseDto("Doctor not found")));

            if (!(bool)doctor)
                return (false, new BadRequestObjectResult(new ErrorResponseDto("Doctor is not active")));

            //// Patient
            var patientCmd = new SqlCommand(
                "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @Id",
                connection);

            patientCmd.Parameters.AddWithValue("@Id", patientId);

            var patient = await patientCmd.ExecuteScalarAsync();

            if (patient == null)
                return (false, new NotFoundObjectResult(new ErrorResponseDto("Patient not found")));

            if (!(bool)patient)
                return (false, new BadRequestObjectResult(new ErrorResponseDto("Patient is not active")));


            return (true, null);
        }
    
        public async Task<(bool ok, IActionResult? error)> ValidateDoctorDateConflict(int doctorId, DateTime appointmentDate)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string DoctorIsScheduledSql = """
                SELECT COUNT(1)
                FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                AND AppointmentDate = @AppointmentDate
                AND Status = 'Scheduled';
            """;

            await using (var command = new SqlCommand(DoctorIsScheduledSql, connection))
            {
                command.Parameters.AddWithValue("@IdDoctor", doctorId);
                command.Parameters.AddWithValue("@AppointmentDate", appointmentDate);
                var scheduled = (int)await command.ExecuteScalarAsync();
                if (scheduled > 0)
                    return (false, new ConflictObjectResult(new ErrorResponseDto("Doctor already has a scheduled appointment at this time.")));
            }

            return (true, null);
        }

        public async Task<(bool ok, IActionResult? error)> ValidateDoctorDateWithAppointmentId(int doctorId, DateTime appointmentDate, int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string DoctorIsScheduledSql = """
                SELECT COUNT(1)
                FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                AND AppointmentDate = @AppointmentDate
                AND Status = 'Scheduled'
                AND IdAppointment <> @IdAppointment;
            """;

            await using (var command = new SqlCommand(DoctorIsScheduledSql, connection))
            {
                command.Parameters.AddWithValue("@IdDoctor", doctorId);
                command.Parameters.AddWithValue("@AppointmentDate", appointmentDate);
                command.Parameters.AddWithValue("@IdAppointment", id);
                var scheduled = (int)await command.ExecuteScalarAsync();
                if (scheduled > 0)
                    return (false, new ConflictObjectResult(new ErrorResponseDto("Doctor already has a scheduled appointment at this time.")));
            }

            return (true, null);
        }

        public async Task<(bool ok, IActionResult? error)> ValidateAppointmentCompletionDateChange(int id, DateTime appointmentDate)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string checkAppointmentSql = """
                SELECT Status, AppointmentDate
                FROM dbo.Appointments
                WHERE IdAppointment = @IdAppointment;
            """;

            await using (var command = new SqlCommand(checkAppointmentSql, connection))
            {
                command.Parameters.AddWithValue("@IdAppointment", id);
                var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (false, new NotFoundObjectResult(new ErrorResponseDto("Appointment does not exist.")));

                var currentStatus = reader.GetString(0);
                var currentDate = reader.GetDateTime(1);

                await reader.CloseAsync();

                if (currentStatus == "Completed" && appointmentDate != currentDate)
                    return (false, new ConflictObjectResult(new ErrorResponseDto("Cannot change completed appointment.")));
            }

            return (true, null);
        }

        public async Task<(bool ok, IActionResult? error)> ValidateAppointmentCompletion(int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string checkAppointmentSql = """
                SELECT Status, AppointmentDate
                FROM dbo.Appointments
                WHERE IdAppointment = @IdAppointment;
            """;

            await using (var command = new SqlCommand(checkAppointmentSql, connection))
            {
                command.Parameters.AddWithValue("@IdAppointment", id);
                var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (false, new NotFoundObjectResult(new ErrorResponseDto("Appointment does not exist.")));

                var currentStatus = reader.GetString(0);
                var currentDate = reader.GetDateTime(1);

                await reader.CloseAsync();

                if (currentStatus == "Completed")
                    return (false, new ConflictObjectResult(new ErrorResponseDto("Appointment already completed")));
            }

            return (true, null);
        }
        
    }

}