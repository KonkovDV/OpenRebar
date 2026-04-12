namespace A101.Domain.Exceptions;

public sealed class ImageSegmentationServiceException : A101DomainException
{
    public ImageSegmentationServiceException(string message, Exception? innerException = null)
        : base("ML_SEGMENTATION_ERROR", message, innerException)
    {
    }
}