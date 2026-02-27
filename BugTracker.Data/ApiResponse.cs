namespace BugTracker.Data
{
    public class ApiResponse<T> : ApiResponse
    {
        public T Data { get; set; }
    }

    public class ApiResponse
    {
        public string ResponseMessage { get; set; }
        public string ResponseCode { get; set; }
    }

    public class ResponseCodes
    {
        public static ApiResponse Success = new() { ResponseCode = "00", ResponseMessage = "Operation Successful" };
        public static ApiResponse FailedValidate = new() { ResponseCode = "06", ResponseMessage = "Validation Failed" };
        public static ApiResponse Processing = new() { ResponseCode = "01", ResponseMessage = "Transaction is being proccessed. Please reconfirm your balance before reattempting transaction." };
        public static ApiResponse DuplicateRecord = new() { ResponseCode = "26", ResponseMessage = "Duplicate record" };
        public static ApiResponse AuthorizedAccess = new() { ResponseCode = "41", ResponseMessage = "Authorized! Login Successful" };
        public static ApiResponse UnAuthorized = new() { ResponseCode = "40", ResponseMessage = "Unauthorized! No record for User name or Password" };
        public static ApiResponse Failed = new() { ResponseCode = "11", ResponseMessage = "Transaction Failed!" };
        public static ApiResponse InvalidEntryDetected = new() { ResponseCode = "02", ResponseMessage = "Invalid Entry" };
        public static ApiResponse EmptyEntryDetected = new() { ResponseCode = "03", ResponseMessage = "Empty Entry Detected" };
        public static ApiResponse TransactionNotAllowed = new() { ResponseCode = "04", ResponseMessage = "Transaction not allowed" };
        public static ApiResponse NumberOfTransactionsNotAllowed = new() { ResponseCode = "05", ResponseMessage = "Number of transactions not allowed, only 5 transactions allowed at a time" };
        public static ApiResponse AccessDenied = new() { ResponseCode = "25", ResponseMessage = "Access Denied. Please liaise with System Administrator" };
        public static ApiResponse InvalidBvn = new() { ResponseCode = "06", ResponseMessage = "Invalid BVN Supplied, Please verify Bvn and Try again" };
        public static ApiResponse MobileNumberNotFound = new() { ResponseCode = "07", ResponseMessage = "Customer has no Mobile Number" };
        public static ApiResponse UnsuccessfulFromBanks = new() { ResponseCode = "08", ResponseMessage = "CBA returned an Error for the Request" };
        public static ApiResponse InvalidAmount = new() { ResponseCode = "09", ResponseMessage = "Amount is invalid" };
        public static ApiResponse ErrorOccured = new() { ResponseCode = "99", ResponseMessage = "An Error Occured. Please Try again" };
        public static ApiResponse UnableToRetrieveDetail = new() { ResponseCode = "90", ResponseMessage = "Unable to Retrieve Detail" };
        public static ApiResponse DuplicatetransactionReference = new() { ResponseCode = "24", ResponseMessage = "Duplicate Transaction Reference " };
        public static ApiResponse NoRecordReturned = new() { ResponseCode = "30", ResponseMessage = "No Record " };
        public static ApiResponse InsufficientBalance = new() { ResponseCode = "51", ResponseMessage = "Insufficient Balance!" };
        public static ApiResponse InvalidNameEnquiryReference = new() { ResponseCode = "91", ResponseMessage = "Invalid Name Enquiry Reference!" };
        public static ApiResponse SystemMalfunction = new() { ResponseCode = "96", ResponseMessage = "System malfunction" };
    }
}
