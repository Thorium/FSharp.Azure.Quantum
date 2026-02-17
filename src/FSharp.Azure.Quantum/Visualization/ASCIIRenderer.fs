namespace FSharp.Azure.Quantum.Visualization

open FSharp.Azure.Quantum

/// ASCII circuit renderer - Qiskit-style quantum circuit diagrams
/// Pure functional implementation with immutable state
module ASCIIRenderer =
    
    /// Box drawing characters (Unicode)
    module private BoxChars =
        let horizontal = "─"
        let vertical = "│"
        let topLeft = "┌"
        let topRight = "┐"
        let bottomLeft = "└"
        let bottomRight = "┘"
        let control = "■"
        let barrier = "░"
    
    /// Gate box representation
    type private GateBox = 
        { Top: string
          Middle: string
          Bottom: string
          Width: int }
    
    /// Quantum wire (one per qubit) - immutable list of segments
    type private Wire = string list
    
    /// Create a gate box with proper borders
    let private gateBox (label: string) : GateBox =
        let width = max 3 (label.Length + 2)
        let padding = (width - label.Length) / 2
        let paddedLabel = label.PadLeft(label.Length + padding).PadRight(width)
        
        { Top = BoxChars.topLeft + String.replicate width BoxChars.horizontal + BoxChars.topRight
          Middle = BoxChars.vertical + paddedLabel + BoxChars.vertical
          Bottom = BoxChars.bottomLeft + String.replicate width BoxChars.horizontal + BoxChars.bottomRight
          Width = width + 2 }
    
    /// Sync all wires to same length
    let private syncWires (wires: Wire list) : Wire list =
        let maxLen = wires |> List.map List.length |> List.max
        wires |> List.map (fun wire ->
            let diff = maxLen - List.length wire
            if diff > 0 
            then wire @ List.replicate diff BoxChars.horizontal
            else wire)
    
    /// Add gate to wires (functional, returns new wires)
    let private addGate numQubits (wires: Wire list) (gate: VisualizationGate) : Wire list =
        let wires' = syncWires wires
        
        match gate with
        | CircuitGate g ->
            match g with
            // Single-qubit gates (no parameters)
            | CircuitBuilder.X qubit | CircuitBuilder.Y qubit | CircuitBuilder.Z qubit 
            | CircuitBuilder.H qubit | CircuitBuilder.S qubit | CircuitBuilder.SDG qubit 
            | CircuitBuilder.T qubit | CircuitBuilder.TDG qubit | CircuitBuilder.Measure qubit
            | CircuitBuilder.Reset qubit ->
                let label = 
                    match g with
                    | CircuitBuilder.X _ -> "X"
                    | CircuitBuilder.Y _ -> "Y"
                    | CircuitBuilder.Z _ -> "Z"
                    | CircuitBuilder.H _ -> "H"
                    | CircuitBuilder.S _ -> "S"
                    | CircuitBuilder.SDG _ -> "S†"
                    | CircuitBuilder.T _ -> "T"
                    | CircuitBuilder.TDG _ -> "T†"
                    | CircuitBuilder.Measure _ -> "M"
                    | CircuitBuilder.Reset _ -> "|0⟩"
                    | _ -> "?"
                
                let box = gateBox label
                wires'
                |> List.mapi (fun i wire ->
                    if i = qubit 
                    then wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    else wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Rotation gates with angles
            | CircuitBuilder.RX (qubit, angle) | CircuitBuilder.RY (qubit, angle) 
            | CircuitBuilder.RZ (qubit, angle) | CircuitBuilder.P (qubit, angle) ->
                let label = 
                    match g with
                    | CircuitBuilder.RX _ -> sprintf "RX(%.2f)" angle
                    | CircuitBuilder.RY _ -> sprintf "RY(%.2f)" angle
                    | CircuitBuilder.RZ _ -> sprintf "RZ(%.2f)" angle
                    | CircuitBuilder.P _ -> sprintf "P(%.2f)" angle
                    | _ -> "?"
                
                let box = gateBox label
                wires'
                |> List.mapi (fun i wire ->
                    if i = qubit 
                    then wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    else wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // U3 gate (3 parameters)
            | CircuitBuilder.U3 (qubit, theta, phi, lambda) ->
                let label = sprintf "U3(%.1f,%.1f,%.1f)" theta phi lambda
                let box = gateBox label
                wires'
                |> List.mapi (fun i wire ->
                    if i = qubit 
                    then wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    else wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Two-qubit controlled gates
            | CircuitBuilder.CNOT (ctrl, targ) | CircuitBuilder.CZ (ctrl, targ) ->
                let label = match g with | CircuitBuilder.CNOT _ -> "X" | _ -> "Z"
                let box = gateBox label
                let minQ = min ctrl targ
                let maxQ = max ctrl targ
                
                wires'
                |> List.mapi (fun i wire ->
                    if i = ctrl then
                        let ctrlStr = BoxChars.horizontal + BoxChars.control + BoxChars.horizontal
                        wire @ [ctrlStr.PadRight(box.Width, '─')] @ List.replicate 3 BoxChars.horizontal
                    elif i = targ then
                        wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    elif i > minQ && i < maxQ then
                        let vLine = 
                            String.replicate (box.Width / 2) BoxChars.horizontal + 
                            BoxChars.vertical + 
                            String.replicate (box.Width / 2) BoxChars.horizontal
                        wire @ [vLine] @ List.replicate 3 BoxChars.horizontal
                    else
                        wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Controlled rotation gates with angles
            | CircuitBuilder.CP (ctrl, targ, angle) | CircuitBuilder.CRX (ctrl, targ, angle) 
            | CircuitBuilder.CRY (ctrl, targ, angle) | CircuitBuilder.CRZ (ctrl, targ, angle) ->
                let label = 
                    match g with
                    | CircuitBuilder.CP _ -> sprintf "P(%.2f)" angle
                    | CircuitBuilder.CRX _ -> sprintf "RX(%.2f)" angle
                    | CircuitBuilder.CRY _ -> sprintf "RY(%.2f)" angle
                    | CircuitBuilder.CRZ _ -> sprintf "RZ(%.2f)" angle
                    | _ -> "?"
                
                let box = gateBox label
                let minQ = min ctrl targ
                let maxQ = max ctrl targ
                
                wires'
                |> List.mapi (fun i wire ->
                    if i = ctrl then
                        let ctrlStr = BoxChars.horizontal + BoxChars.control + BoxChars.horizontal
                        wire @ [ctrlStr.PadRight(box.Width, '─')] @ List.replicate 3 BoxChars.horizontal
                    elif i = targ then
                        wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    elif i > minQ && i < maxQ then
                        let vLine = 
                            String.replicate (box.Width / 2) BoxChars.horizontal + 
                            BoxChars.vertical + 
                            String.replicate (box.Width / 2) BoxChars.horizontal
                        wire @ [vLine] @ List.replicate 3 BoxChars.horizontal
                    else
                        wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // SWAP gate
            | CircuitBuilder.SWAP (q1, q2) ->
                let box = gateBox "×"
                let minQ = min q1 q2
                let maxQ = max q1 q2
                
                wires'
                |> List.mapi (fun i wire ->
                    if i = q1 || i = q2 then
                        wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    elif i > minQ && i < maxQ then
                        let vLine = 
                            String.replicate (box.Width / 2) BoxChars.horizontal + 
                            BoxChars.vertical + 
                            String.replicate (box.Width / 2) BoxChars.horizontal
                        wire @ [vLine] @ List.replicate 3 BoxChars.horizontal
                    else
                        wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Toffoli (CCX) gate
            | CircuitBuilder.CCX (ctrl1, ctrl2, targ) ->
                let box = gateBox "X"
                let qubits = [ctrl1; ctrl2; targ] |> List.sort
                // Safe: qubits list has exactly 3 elements
                let (minQ, maxQ) = 
                    match qubits with
                    | minQ :: _ :: maxQ :: [] -> (minQ, maxQ)
                    | _ -> failwith "Internal error: CCX should have exactly 3 qubits"
                
                wires'
                |> List.mapi (fun i wire ->
                    if i = ctrl1 || i = ctrl2 then
                        let ctrlStr = BoxChars.horizontal + BoxChars.control + BoxChars.horizontal
                        wire @ [ctrlStr.PadRight(box.Width, '─')] @ List.replicate 3 BoxChars.horizontal
                    elif i = targ then
                        wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    elif i > minQ && i < maxQ then
                        let vLine = 
                            String.replicate (box.Width / 2) BoxChars.horizontal + 
                            BoxChars.vertical + 
                            String.replicate (box.Width / 2) BoxChars.horizontal
                        wire @ [vLine] @ List.replicate 3 BoxChars.horizontal
                    else
                        wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Multi-controlled Z gate
            | CircuitBuilder.MCZ (ctrls, targ) ->
                let box = gateBox "Z"
                let allQubits = targ :: ctrls |> List.sort
                // Safe: Extract min and max with pattern matching
                let (minQ, maxQ) = 
                    match allQubits with
                    | [] -> failwith "Internal error: MCZ should have at least 1 qubit"
                    | [single] -> (single, single)
                    | minQ :: rest -> (minQ, List.last rest)  // rest is non-empty
                
                wires'
                |> List.mapi (fun i wire ->
                    if List.contains i ctrls then
                        let ctrlStr = BoxChars.horizontal + BoxChars.control + BoxChars.horizontal
                        wire @ [ctrlStr.PadRight(box.Width, '─')] @ List.replicate 3 BoxChars.horizontal
                    elif i = targ then
                        wire @ [box.Middle] @ List.replicate 3 BoxChars.horizontal
                    elif i > minQ && i < maxQ then
                        let vLine = 
                            String.replicate (box.Width / 2) BoxChars.horizontal + 
                            BoxChars.vertical + 
                            String.replicate (box.Width / 2) BoxChars.horizontal
                        wire @ [vLine] @ List.replicate 3 BoxChars.horizontal
                    else
                        wire @ [String.replicate box.Width BoxChars.horizontal] @ List.replicate 3 BoxChars.horizontal)
            
            // Barrier gate from CircuitBuilder
            | CircuitBuilder.Barrier qubits ->
                wires'
                |> List.mapi (fun i wire ->
                    if List.contains i qubits
                    then wire @ [BoxChars.barrier; BoxChars.barrier; BoxChars.barrier]
                    else wire @ List.replicate 3 BoxChars.horizontal)
        
        // Barrier (visualization-only)
        | Barrier qubits ->
            wires'
            |> List.mapi (fun i wire ->
                if List.contains i qubits
                then wire @ [BoxChars.barrier; BoxChars.barrier; BoxChars.barrier]
                else wire @ List.replicate 3 BoxChars.horizontal)
    
    /// Render quantum circuit to ASCII string
    let render (numQubits: int) (gates: VisualizationGate list) : string =
        // Initialize wires with qubit labels
        let initialWires = List.init numQubits (fun i -> [$"q_{i}: "])
        
        // Fold gates to build wires
        let finalWires = gates |> List.fold (addGate numQubits) initialWires
        
        // Convert to string (join each wire's segments)
        finalWires
        |> List.map (fun wire -> String.concat "" wire)
        |> String.concat "\n"
    
    /// Render with configuration
    let renderWithConfig (config: VisualizationConfig) (numQubits: int) (gates: VisualizationGate list) : string =
        let filteredGates =
            gates
            |> List.filter (fun gate ->
                match gate with
                | CircuitGate (CircuitBuilder.Measure _) -> config.ShowMeasurements
                | Barrier _ -> config.ShowBarriers
                | _ -> true)
        
        render numQubits filteredGates

