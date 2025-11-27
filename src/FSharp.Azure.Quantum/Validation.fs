namespace FSharp.Azure.Quantum

/// <summary>
/// Shared validation utilities used across FSharp.Azure.Quantum modules
/// </summary>
module Validation =

    /// <summary>
    /// Standard validation result with validity status and messages
    /// </summary>
    type ValidationResult = {
        /// Whether validation passed
        IsValid: bool
        
        /// Error or warning messages (empty if IsValid = true)
        Messages: string list
    }

    /// <summary>
    /// Create a successful validation result
    /// </summary>
    let success : ValidationResult =
        { IsValid = true; Messages = [] }

    /// <summary>
    /// Create a failed validation result with error messages
    /// </summary>
    let failure (errors: string list) : ValidationResult =
        { IsValid = false; Messages = errors }

    /// <summary>
    /// Create a failed validation result with a single error message
    /// </summary>
    let failWith (error: string) : ValidationResult =
        { IsValid = false; Messages = [error] }

    /// <summary>
    /// Combine multiple validation results (all must pass for success)
    /// </summary>
    let combine (results: ValidationResult list) : ValidationResult =
        let allValid = results |> List.forall (fun r -> r.IsValid)
        let allMessages = results |> List.collect (fun r -> r.Messages)
        { IsValid = allValid; Messages = allMessages }

    /// <summary>
    /// Map validation result to Result&lt;'T, string list&gt;
    /// </summary>
    let toResult (value: 'T) (validation: ValidationResult) : Result<'T, string list> =
        if validation.IsValid then
            Ok value
        else
            Error validation.Messages

    /// <summary>
    /// Format validation errors as a multi-line string
    /// </summary>
    let formatErrors (validation: ValidationResult) : string =
        if validation.IsValid then
            "Validation passed"
        else
            let header = sprintf "Validation failed with %d error(s):" validation.Messages.Length
            let messages = validation.Messages |> List.mapi (fun i msg -> sprintf "  %d. %s" (i + 1) msg)
            String.concat "\n" (header :: messages)
