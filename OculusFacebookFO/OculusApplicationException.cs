namespace OculusFacebookFO;

public class OculusApplicationException : ApplicationException
{
    /// <inheritdoc />
    public OculusApplicationException(string? message = null, Exception? innerException = null) 
        : base(message, innerException)
    {
    }
}