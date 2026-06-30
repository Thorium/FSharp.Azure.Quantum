namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum.CircuitBuilder

/// QIR (Quantum Intermediate Representation) emission.
///
/// Emits a circuit as QIR **base-profile** textual LLVM IR — the representation
/// Azure Quantum accepts as a hardware-agnostic submission format. Each gate maps
/// to its standard `__quantum__qis__*` intrinsic; measurements use `mz` plus
/// `__quantum__rt__result_record_output`, and qubits/results are static integer
/// references (`inttoptr`) as required by the base profile.
///
/// Gates without a direct base-profile intrinsic (e.g. U3, CCX, controlled
/// rotations) must be decomposed first via `GateTranspiler`; otherwise emission
/// returns an Error naming the unsupported gate.
module QirEmitter =

    // Static base-profile references.
    let private qubitRef (q: int) = sprintf "%%Qubit* inttoptr (i64 %d to %%Qubit*)" q
    let private resultRef (r: int) = sprintf "%%Result* inttoptr (i64 %d to %%Result*)" r
    // LLVM-valid double literal: exact 64-bit IEEE-754 hex (decimal is only valid
    // for exactly-representable values, so always use the hex form).
    let private dbl (x: float) = sprintf "0x%016X" (BitConverter.DoubleToInt64Bits x)

    // Declarations for the intrinsics we may emit, keyed so we only declare those used.
    let private declarations =
        [ "h", "declare void @__quantum__qis__h__body(%Qubit*)"
          "x", "declare void @__quantum__qis__x__body(%Qubit*)"
          "y", "declare void @__quantum__qis__y__body(%Qubit*)"
          "z", "declare void @__quantum__qis__z__body(%Qubit*)"
          "s", "declare void @__quantum__qis__s__body(%Qubit*)"
          "sadj", "declare void @__quantum__qis__s__adj(%Qubit*)"
          "t", "declare void @__quantum__qis__t__body(%Qubit*)"
          "tadj", "declare void @__quantum__qis__t__adj(%Qubit*)"
          "rx", "declare void @__quantum__qis__rx__body(double, %Qubit*)"
          "ry", "declare void @__quantum__qis__ry__body(double, %Qubit*)"
          "rz", "declare void @__quantum__qis__rz__body(double, %Qubit*)"
          "cnot", "declare void @__quantum__qis__cnot__body(%Qubit*, %Qubit*)"
          "cz", "declare void @__quantum__qis__cz__body(%Qubit*, %Qubit*)"
          "swap", "declare void @__quantum__qis__swap__body(%Qubit*, %Qubit*)"
          "reset", "declare void @__quantum__qis__reset__body(%Qubit*)"
          "mz", "declare void @__quantum__qis__mz__body(%Qubit*, %Result*)"
          "record", "declare void @__quantum__rt__result_record_output(%Result*, i8*)" ]
        |> Map.ofList

    /// Lower a single gate to (instruction lines, used-declaration keys), allocating
    /// result ids for measurements via `nextResult`.
    let private lowerGate (nextResult: unit -> int) (gate: Gate) : Result<string list * string list, string> =
        let one name q =
            Ok ([ sprintf "  call void @__quantum__qis__%s(%s)" name (qubitRef q) ], [ ])
        // single-qubit, no parameter: intrinsic body call + its declaration key
        let s1 declKey intrinsic q =
            Ok ([ sprintf "  call void @__quantum__qis__%s(%s)" intrinsic (qubitRef q) ], [ declKey ])
        // single-qubit rotation
        let rot declKey intrinsic (q: int) (theta: float) =
            Ok ([ sprintf "  call void @__quantum__qis__%s(double %s, %s)" intrinsic (dbl theta) (qubitRef q) ], [ declKey ])
        // two-qubit
        let two declKey intrinsic a b =
            Ok ([ sprintf "  call void @__quantum__qis__%s(%s, %s)" intrinsic (qubitRef a) (qubitRef b) ], [ declKey ])
        ignore one
        match gate with
        | H q -> s1 "h" "h__body" q
        | X q -> s1 "x" "x__body" q
        | Y q -> s1 "y" "y__body" q
        | Z q -> s1 "z" "z__body" q
        | S q -> s1 "s" "s__body" q
        | SDG q -> s1 "sadj" "s__adj" q
        | T q -> s1 "t" "t__body" q
        | TDG q -> s1 "tadj" "t__adj" q
        | RX(q, t) -> rot "rx" "rx__body" q t
        | RY(q, t) -> rot "ry" "ry__body" q t
        | RZ(q, t) -> rot "rz" "rz__body" q t
        // P(θ) = Rz(θ) up to a global phase, which is unobservable.
        | P(q, t) -> rot "rz" "rz__body" q t
        | CNOT(c, t) -> two "cnot" "cnot__body" c t
        | CZ(c, t) -> two "cz" "cz__body" c t
        | SWAP(a, b) -> two "swap" "swap__body" a b
        | Reset q -> s1 "reset" "reset__body" q
        | Measure q ->
            let r = nextResult ()
            Ok([ sprintf "  call void @__quantum__qis__mz__body(%s, %s)" (qubitRef q) (resultRef r)
                 sprintf "  call void @__quantum__rt__result_record_output(%s, i8* null)" (resultRef r) ],
               [ "mz"; "record" ])
        | Barrier _ -> Ok([], [])  // no-op in QIR
        | other ->
            Error(sprintf "QIR base profile has no direct intrinsic for gate '%s'; run GateTranspiler first" (getGateName other))

    /// Emit a circuit as QIR base-profile textual LLVM IR, or Error naming the first
    /// gate that has no base-profile intrinsic.
    let emit (circuit: Circuit) : Result<string, string> =
        let mutable resultCount = 0
        let nextResult () =
            let r = resultCount
            resultCount <- resultCount + 1
            r

        let folded =
            (Ok([], Set.empty), getGates circuit)
            ||> List.fold (fun acc gate ->
                match acc with
                | Error e -> Error e
                | Ok(lines, decls) ->
                    match lowerGate nextResult gate with
                    | Error e -> Error e
                    | Ok(gLines, gDecls) -> Ok(lines @ gLines, Set.union decls (Set.ofList gDecls)))

        match folded with
        | Error e -> Error e
        | Ok(bodyLines, usedDeclKeys) ->
            let numQubits = max 1 circuit.QubitCount
            let declLines =
                usedDeclKeys
                |> Set.toList
                |> List.choose (fun k -> Map.tryFind k declarations)
            let sb = System.Text.StringBuilder()
            let line (s: string) = sb.AppendLine(s) |> ignore
            line "; QIR base profile — generated by FSharp.Azure.Quantum"
            line "%Qubit = type opaque"
            line "%Result = type opaque"
            line ""
            line "define void @main() #0 {"
            line "entry:"
            bodyLines |> List.iter line
            line "  ret void"
            line "}"
            line ""
            declLines |> List.iter line
            line ""
            line (sprintf "attributes #0 = { \"entry_point\" \"qir_profiles\"=\"base_profile\" \"required_num_qubits\"=\"%d\" \"required_num_results\"=\"%d\" \"output_labeling_schema\" }" numQubits resultCount)
            Ok(sb.ToString())
