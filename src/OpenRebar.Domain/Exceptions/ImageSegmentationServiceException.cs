namespace OpenRebar.Domain.Exceptions;

public sealed class ImageSegmentationServiceException : OpenRebarDomainException
{
    public ImageSegmentationServiceException(string message, Exception? innerException = null)
        : base("ML_SEGMENTATION_ERROR", message, innerException)
    {
    }
}