namespace FSharp.Azure.Quantum.Visualization

open FSharp.Azure.Quantum

/// Mermaid diagram renderer - For quantum circuits and problem graphs
/// Generates Mermaid.js compatible markdown for GitHub/GitLab/VS Code
module MermaidRenderer =
    
    /// Mermaid sequence diagram for quantum circuit (temporal view)
    module Sequence =
        
        let private participant i = 
            $"    participant q{i} as Qubit {i} (|0⟩)"
        
        let private gateToLines gate =
            match gate with
            | CircuitGate g ->
                match g with
                // Single-qubit gates
                | CircuitBuilder.X qubit ->
                    [ $"    Note over q{qubit}: X"
                      $"    q{qubit}->>q{qubit}: Apply X" ]
                | CircuitBuilder.Y qubit ->
                    [ $"    Note over q{qubit}: Y"
                      $"    q{qubit}->>q{qubit}: Apply Y" ]
                | CircuitBuilder.Z qubit ->
                    [ $"    Note over q{qubit}: Z"
                      $"    q{qubit}->>q{qubit}: Apply Z" ]
                | CircuitBuilder.H qubit ->
                    [ $"    Note over q{qubit}: H"
                      $"    q{qubit}->>q{qubit}: Apply H" ]
                | CircuitBuilder.S qubit ->
                    [ $"    Note over q{qubit}: S"
                      $"    q{qubit}->>q{qubit}: Apply S" ]
                | CircuitBuilder.SDG qubit ->
                    [ $"    Note over q{qubit}: S†"
                      $"    q{qubit}->>q{qubit}: Apply S†" ]
                | CircuitBuilder.T qubit ->
                    [ $"    Note over q{qubit}: T"
                      $"    q{qubit}->>q{qubit}: Apply T" ]
                | CircuitBuilder.TDG qubit ->
                    [ $"    Note over q{qubit}: T†"
                      $"    q{qubit}->>q{qubit}: Apply T†" ]
                | CircuitBuilder.Measure qubit ->
                    [ $"    Note over q{qubit}: Measure"
                      $"    q{qubit}->>q{qubit}: Measure → Classical" ]
                
                // Rotation gates
                | CircuitBuilder.RX (qubit, angle) ->
                    [ $"    Note over q{qubit}: RX({angle:F2})"
                      $"    q{qubit}->>q{qubit}: Apply RX" ]
                | CircuitBuilder.RY (qubit, angle) ->
                    [ $"    Note over q{qubit}: RY({angle:F2})"
                      $"    q{qubit}->>q{qubit}: Apply RY" ]
                | CircuitBuilder.RZ (qubit, angle) ->
                    [ $"    Note over q{qubit}: RZ({angle:F2})"
                      $"    q{qubit}->>q{qubit}: Apply RZ" ]
                | CircuitBuilder.P (qubit, angle) ->
                    [ $"    Note over q{qubit}: P({angle:F2})"
                      $"    q{qubit}->>q{qubit}: Apply P" ]
                | CircuitBuilder.U3 (qubit, theta, phi, lambda) ->
                    [ $"    Note over q{qubit}: U3({theta:F1},{phi:F1},{lambda:F1})"
                      $"    q{qubit}->>q{qubit}: Apply U3" ]
                
                // Two-qubit controlled gates
                | CircuitBuilder.CNOT (ctrl, targ) ->
                    [ $"    Note over q{ctrl},q{targ}: C-X"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: X" ]
                | CircuitBuilder.CZ (ctrl, targ) ->
                    [ $"    Note over q{ctrl},q{targ}: C-Z"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: Z" ]
                | CircuitBuilder.CP (ctrl, targ, angle) ->
                    [ $"    Note over q{ctrl},q{targ}: CP({angle:F2})"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: P" ]
                | CircuitBuilder.CRX (ctrl, targ, angle) ->
                    [ $"    Note over q{ctrl},q{targ}: CRX({angle:F2})"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: RX" ]
                | CircuitBuilder.CRY (ctrl, targ, angle) ->
                    [ $"    Note over q{ctrl},q{targ}: CRY({angle:F2})"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: RY" ]
                | CircuitBuilder.CRZ (ctrl, targ, angle) ->
                    [ $"    Note over q{ctrl},q{targ}: CRZ({angle:F2})"
                      $"    q{ctrl}->>q{targ}: Control"
                      $"    q{targ}->>q{targ}: RZ" ]
                | CircuitBuilder.SWAP (q1, q2) ->
                    [ $"    Note over q{q1},q{q2}: SWAP"
                      $"    q{q1}<<->>q{q2}: Exchange" ]
                
                // Multi-qubit gates
                | CircuitBuilder.CCX (ctrl1, ctrl2, targ) ->
                    let qubits = [ctrl1; ctrl2; targ] |> List.sort
                    let minQ = List.head qubits
                    let maxQ = List.last qubits
                    [ $"    Note over q{minQ},q{maxQ}: Toffoli (CCX)"
                      $"    q{ctrl1}->>q{targ}: Control 1"
                      $"    q{ctrl2}->>q{targ}: Control 2"
                      $"    q{targ}->>q{targ}: X" ]
                | CircuitBuilder.MCZ (ctrls, targ) ->
                    let allQubits = targ :: ctrls |> List.sort
                    let minQ = List.head allQubits
                    let maxQ = List.last allQubits
                    [ $"    Note over q{minQ},q{maxQ}: Multi-Controlled Z"
                      yield! ctrls |> List.map (fun c -> $"    q{c}->>q{targ}: Control")
                      $"    q{targ}->>q{targ}: Z" ]
            
            | Barrier qubits ->
                let qubitList = qubits |> List.map (fun q -> $"q{q}") |> String.concat ","
                [ $"    Note over {qubitList}: Barrier" ]
        
        let render numQubits gates =
            [ "```mermaid"
              "sequenceDiagram"
              yield! List.init numQubits participant
              ""
              yield! gates |> List.collect gateToLines
              "```" ]
            |> String.concat "\n"
    
    /// Mermaid flowchart for quantum circuit (data flow view)
    /// TODO: Update to support all CircuitBuilder.Gate types
    module Flowchart =
        
        type private NodeId = NodeId of int
        type private QubitState = NodeId list
        
        let private nextId (NodeId n) = NodeId (n + 1)
        
        let private initialState numQubits =
            let nodes = List.init numQubits (fun i -> NodeId i, $"|0⟩_{i}")
            let lines = nodes |> List.map (fun (NodeId id, label) -> $"    n{id}[\"{label}\"]")
            let states = nodes |> List.map fst
            (NodeId numQubits, states, lines)
        
        let private getGateLabel = function
            | CircuitBuilder.X _ -> "X"
            | CircuitBuilder.Y _ -> "Y"
            | CircuitBuilder.Z _ -> "Z"
            | CircuitBuilder.H _ -> "H"
            | CircuitBuilder.S _ -> "S"
            | CircuitBuilder.SDG _ -> "S†"
            | CircuitBuilder.T _ -> "T"
            | CircuitBuilder.TDG _ -> "T†"
            | CircuitBuilder.RX (_, a) -> $"RX({a:F2})"
            | CircuitBuilder.RY (_, a) -> $"RY({a:F2})"
            | CircuitBuilder.RZ (_, a) -> $"RZ({a:F2})"
            | CircuitBuilder.P (_, a) -> $"P({a:F2})"
            | CircuitBuilder.U3 (_, t, p, l) -> $"U3({t:F1},{p:F1},{l:F1})"
            | CircuitBuilder.CNOT _ -> "CNOT"
            | CircuitBuilder.CZ _ -> "CZ"
            | CircuitBuilder.CP (_, _, a) -> $"CP({a:F2})"
            | CircuitBuilder.CRX (_, _, a) -> $"CRX({a:F2})"
            | CircuitBuilder.CRY (_, _, a) -> $"CRY({a:F2})"
            | CircuitBuilder.CRZ (_, _, a) -> $"CRZ({a:F2})"
            | CircuitBuilder.SWAP _ -> "SWAP"
            | CircuitBuilder.CCX _ -> "CCX"
            | CircuitBuilder.MCZ _ -> "MCZ"
            | CircuitBuilder.Measure _ -> "Measure"
        
        let private processGate (currentId, qubitStates: QubitState, lines) gate =
            match gate with
            | CircuitGate g ->
                match g with
                | CircuitBuilder.X qubit | CircuitBuilder.Y qubit | CircuitBuilder.Z qubit 
                | CircuitBuilder.H qubit | CircuitBuilder.S qubit | CircuitBuilder.SDG qubit 
                | CircuitBuilder.T qubit | CircuitBuilder.TDG qubit 
                | CircuitBuilder.RX (qubit, _) | CircuitBuilder.RY (qubit, _) | CircuitBuilder.RZ (qubit, _) 
                | CircuitBuilder.P (qubit, _) | CircuitBuilder.U3 (qubit, _, _, _) ->
                    let gateId = currentId
                    let nextStateId = nextId gateId
                    let (NodeId prevId) = qubitStates.[qubit]
                    let (NodeId gId) = gateId
                    let (NodeId nsId) = nextStateId
                    let label = getGateLabel g
                    
                    let newLines = 
                        [ $"    n{gId}[{label}]"
                          $"    n{prevId} --> n{gId}"
                          $"    n{gId} --> n{nsId}" ]
                    
                    let newStates = qubitStates |> List.mapi (fun i s -> if i = qubit then nextStateId else s)
                    
                    (nextId nextStateId, newStates, lines @ newLines)
                
                | CircuitBuilder.CNOT (ctrl, targ) | CircuitBuilder.CZ (ctrl, targ) 
                | CircuitBuilder.CP (ctrl, targ, _) | CircuitBuilder.CRX (ctrl, targ, _) 
                | CircuitBuilder.CRY (ctrl, targ, _) | CircuitBuilder.CRZ (ctrl, targ, _) ->
                    let ctrlId = currentId
                    let gateId = nextId ctrlId
                    let nextCtrlId = nextId gateId
                    let nextTargId = nextId nextCtrlId
                    
                    let (NodeId prevCtrlId) = qubitStates.[ctrl]
                    let (NodeId prevTargId) = qubitStates.[targ]
                    let (NodeId cId) = ctrlId
                    let (NodeId gId) = gateId
                    let (NodeId ncId) = nextCtrlId
                    let (NodeId ntId) = nextTargId
                    let label = getGateLabel g
                    
                    let newLines =
                        [ $"    n{cId}{{●}}"
                          $"    n{gId}[{label}]"
                          $"    n{prevCtrlId} --> n{cId}"
                          $"    n{cId} -.->|control| n{gId}"
                          $"    n{prevTargId} --> n{gId}"
                          $"    n{cId} --> n{ncId}"
                          $"    n{gId} --> n{ntId}" ]
                    
                    let newStates = 
                        qubitStates 
                        |> List.mapi (fun i s -> 
                            if i = ctrl then nextCtrlId
                            elif i = targ then nextTargId
                            else s)
                    
                    (nextId nextTargId, newStates, lines @ newLines)
                
                | CircuitBuilder.Measure qubit ->
                    let mId = currentId
                    let (NodeId prevId) = qubitStates.[qubit]
                    let (NodeId mIdVal) = mId
                    
                    let newLines = 
                        [ $"    n{mIdVal}[📊 Measure]"
                          $"    n{prevId} --> n{mIdVal}" ]
                    
                    (nextId mId, qubitStates, lines @ newLines)
                
                | _ ->
                    // Skip gates not yet fully implemented (SWAP, CCX, MCZ)
                    (currentId, qubitStates, lines)
            
            | Barrier _ ->
                // Skip barriers in flowchart (doesn't affect data flow)
                (currentId, qubitStates, lines)
        
        let render numQubits gates =
            let (startId, startStates, initLines) = initialState numQubits
            let (_, _, allLines) = gates |> List.fold processGate (startId, startStates, initLines)
            
            [ "```mermaid"
              "flowchart LR"
              yield! allLines
              "```" ]
            |> String.concat "\n"
    
    /// Mermaid graph for problem graphs (Graph Coloring, MaxCut, TSP, etc.)
    module Graph =
        
        /// Node with optional color/label
        type Node = { Name: string; Color: string option; Label: string option }
        
        /// Edge between nodes
        type Edge = { From: string; To: string; Label: string option; Weight: float option }
        
        let private nodeToLines (node: Node) =
            let displayLabel = 
                match node.Label with
                | Some label -> $"Node {node.Name}<br/>{label}"
                | None -> $"Node {node.Name}"
            
            let nodeLine = $"    {node.Name}[\"{displayLabel}\"]"
            
            match node.Color with
            | Some color ->
                [ nodeLine
                  $"    style {node.Name} fill:#{color}" ]
            | None ->
                [ nodeLine ]
        
        let private edgeToLine (edge: Edge) =
            let edgeLabel = 
                match edge.Label, edge.Weight with
                | Some label, Some weight -> $"|{label} ({weight})|"
                | Some label, None -> $"|{label}|"
                | None, Some weight -> $"|{weight}|"
                | None, None -> ""
            
            $"    {edge.From} ---{edgeLabel} {edge.To}"
        
        let render (nodes: Node list) (edges: Edge list) =
            [ "```mermaid"
              "graph TD"
              yield! nodes |> List.collect nodeToLines
              yield! edges |> List.map edgeToLine
              "```" ]
            |> String.concat "\n"
        
        /// Convenience function for simple (name, color) tuples
        let renderSimple (nodeList: (string * string option) list) (edgeList: (string * string) list) =
            let nodes = nodeList |> List.map (fun (name, color) -> { Name = name; Color = color; Label = None })
            let edges = edgeList |> List.map (fun (from', to') -> { From = from'; To = to'; Label = None; Weight = None })
            render nodes edges
        
        /// Create node with color
        let node name color = { Name = name; Color = Some color; Label = None }
        
        /// Create node with label
        let nodeWithLabel name label = { Name = name; Color = None; Label = Some label }
        
        /// Create node with color and label
        let nodeWithColorAndLabel name color label = { Name = name; Color = Some color; Label = Some label }
        
        /// Create simple edge
        let edge from' to' = { From = from'; To = to'; Label = None; Weight = None }
        
        /// Create labeled edge
        let labeledEdge from' to' label = { From = from'; To = to'; Label = Some label; Weight = None }
        
        /// Create weighted edge
        let weightedEdge from' to' weight = { From = from'; To = to'; Label = None; Weight = Some weight }
