namespace TestApp.Core.Types

type public ErrorCode =
    | BadRequest = 400
    | Forbidden = 403
    | NotFound = 404
    | MethodNotAllowed = 405
    | NotAcceptable = 406
    | RequestTimeout = 408
    | Conflict = 409
    | Gone = 410
    | LengthRequired = 411
    | PayloadTooLarge = 413
    | URITooLong = 414
    | UnsupportedMediaType = 415
    | ExpectationFailed = 417
    | UpgradeRequired = 426
    | InternalServerError = 500

type public Error = {
    Code: ErrorCode
    Message: string
}
