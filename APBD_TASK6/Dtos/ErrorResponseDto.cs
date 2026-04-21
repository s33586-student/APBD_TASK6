namespace APBD_TASK6.Dtos
{
    public class ErrorResponseDto
    {
        public string Message { get; set; } = string.Empty;

        public ErrorResponseDto() { }

        public ErrorResponseDto(string message)
        {
            this.Message = message;
        }
    }
}
