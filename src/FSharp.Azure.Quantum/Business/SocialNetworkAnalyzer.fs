namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// High-level social network analysis using quantum algorithms.
/// </summary>
/// <remarks>
/// **Business Use Cases:**
/// - Marketing: Identify influencer groups and communities
/// - Security: Detect fraud rings and collusion networks
/// - HR: Analyze team dynamics and collaboration patterns
/// - Healthcare: Track disease outbreak clusters
/// - E-commerce: Find product recommendation groups
/// - Network monitoring: Find minimum observer set covering all connections
/// - Mentoring: Find optimal 1:1 pairings in organizations
/// 
/// **Quantum Algorithms:**
/// - **Grover's search** (`findCommunities`): Finds cliques of a specific size. Exact search.
/// - **QAOA optimization** (`findLargestCommunity`, `findMonitorSet`, `findPairings`):
///   Finds optimal solutions via QUBO formulation. Approximate optimization.
/// 
/// **Example:**
/// ```fsharp
/// let analysis = socialNetwork {
///     person "Alice"
///     person "Bob"
///     person "Carol"
///     connection "Alice" "Bob"
///     connection "Bob" "Carol"
///     connection "Carol" "Alice"
///     findLargestCommunity
///     backend myBackend
/// }
/// ```
/// </remarks>
module SocialNetworkAnalyzer =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Person identifier in the social network
    type PersonId = string
    
    /// Connection between two people
    type Connection = {
        Person1: PersonId
        Person2: PersonId
    }
    
    /// Analysis mode determines which quantum algorithm and problem formulation to use
    type AnalysisMode =
        /// Find communities of exactly a given size (Grover's search with clique oracle)
        | FindCommunities of minSize: int
        /// Find the largest fully-connected group (QAOA max clique optimization)
        | FindLargestCommunity
        /// Find the minimum set of people covering all connections (QAOA min vertex cover)
        | FindMonitorSet
        /// Find optimal 1:1 pairings (QAOA max weight matching)
        | FindPairings
    
    /// Social network analysis problem configuration
    type SocialNetworkProblem = {
        /// All people in the network
        People: PersonId list
        
        /// Connections (friendships, follows, etc.)
        Connections: Connection list
        
        /// Minimum community size to find (legacy, used when AnalysisMode = None)
        MinCommunitySize: int option
        
        /// Analysis mode (None defaults to FindCommunities if MinCommunitySize is set)
        Mode: AnalysisMode option
        
        /// Quantum backend (None = classical algorithm, Some = quantum acceleration)
        Backend: IQuantumBackend option
        
        /// Number of measurement shots (default: 1000)
        Shots: int
    }
    
    /// Community found in the network
    type Community = {
        /// People in this community
        Members: PersonId list
        
        /// Strength (how interconnected): 1.0 = fully connected (clique)
        Strength: float
        
        /// Number of connections within community
        InternalConnections: int
    }
    
    /// A pairing of two people found by matching analysis
    type Pairing = {
        /// First person in the pair
        Person1: PersonId
        /// Second person in the pair
        Person2: PersonId
        /// Weight of the connection (higher = stronger match)
        Weight: float
    }
    
    /// Result of social network analysis
    type SocialNetworkResult = {
        /// Communities found (populated by FindCommunities and FindLargestCommunity modes)
        Communities: Community list
        
        /// Monitor set — minimum people covering all connections (populated by FindMonitorSet)
        MonitorSet: PersonId list
        
        /// Optimal pairings (populated by FindPairings)
        Pairings: Pairing list
        
        /// Total people analyzed
        TotalPeople: int
        
        /// Total connections analyzed
        TotalConnections: int
        
        /// Execution message
        Message: string
    }
    
    // ========================================================================
    // HELPERS
    // ========================================================================
    
    /// Create a person-to-index mapping
    let private createPersonIndex (people: PersonId list) : Map<PersonId, int> =
        people
        |> List.mapi (fun i person -> (person, i))
        |> Map.ofList
    
    /// Convert social network to graph representation
    let private toGraph (problem: SocialNetworkProblem) : GraphColoringGraph =
        let personIndex = createPersonIndex problem.People
        
        let edges =
            problem.Connections
            |> List.choose (fun conn ->
                match Map.tryFind conn.Person1 personIndex, Map.tryFind conn.Person2 personIndex with
                | Some idx1, Some idx2 -> Some (idx1, idx2)
                | _ -> None  // Skip connections to unknown people
            )
        
        {
            NumVertices = problem.People.Length
            Edges = edges
        }
    
    /// Calculate community strength (how interconnected)
    let private calculateStrength (members: PersonId list) (connections: Connection list) : float =
        if members.Length < 2 then 0.0
        else
            let memberSet = Set.ofList members
            let internalConnections =
                connections
                |> List.filter (fun conn ->
                    memberSet.Contains(conn.Person1) && memberSet.Contains(conn.Person2))
                |> List.length
            
            // Strength = actual connections / maximum possible connections
            let maxConnections = members.Length * (members.Length - 1) / 2
            if maxConnections = 0 then 0.0
            else float internalConnections / float maxConnections
    
    /// Find communities using quantum clique detection
    let private findCommunitiesQuantum (backend: IQuantumBackend) (problem: SocialNetworkProblem) : QuantumResult<Community list> =
        match problem.MinCommunitySize with
        | None ->
            Error (QuantumError.ValidationError ("MinCommunitySize", "must be specified"))
        | Some size when size < 2 ->
            Error (QuantumError.ValidationError ("MinCommunitySize", "must be at least 2"))
        | Some cliqueSize ->
            let graph = toGraph problem
            let config = { Graph = graph; CliqueSize = cliqueSize }
            
            match cliqueOracle config with
            | Error err -> Error err
            | Ok oracle ->
                // Configure Grover search with specified shots
                let groverConfig = { Grover.defaultConfig with Shots = problem.Shots }
                
                // Run Grover's search algorithm
                match Grover.search oracle backend groverConfig with
                | Error err -> Error err
                | Ok groverResult ->
                    // Decode bitstring solutions to communities
                    let communities =
                        groverResult.Solutions
                        |> List.map (fun bitstring ->
                            // Extract selected people from bitstring
                            let selectedIndices =
                                [0 .. problem.People.Length - 1]
                                |> List.filter (fun i -> (bitstring >>> i) &&& 1 = 1)
                            
                            let members =
                                selectedIndices
                                |> List.map (fun idx -> problem.People.[idx])
                            
                            let strength = calculateStrength members problem.Connections
                            let internalConns =
                                let memberSet = Set.ofList members
                                problem.Connections
                                |> List.filter (fun conn ->
                                    memberSet.Contains(conn.Person1) && memberSet.Contains(conn.Person2))
                                |> List.length
                            
                            {
                                Members = members
                                Strength = strength
                                InternalConnections = internalConns
                            }
                        )
                    
                    Ok communities
    
    // ========================================================================
    // QAOA-BASED ANALYSIS FUNCTIONS
    // ========================================================================
    
    /// Find the largest fully-connected group using QAOA max clique optimization
    let private findLargestCommunityQaoa (backend: IQuantumBackend) (problem: SocialNetworkProblem) : QuantumResult<Community list> =
        let personIndex = createPersonIndex problem.People
        let edges =
            problem.Connections
            |> List.choose (fun conn ->
                match Map.tryFind conn.Person1 personIndex, Map.tryFind conn.Person2 personIndex with
                | Some idx1, Some idx2 -> Some (idx1, idx2)
                | _ -> None)
        
        let cliqueProblem : QuantumCliqueSolver.Problem = {
            Vertices =
                problem.People
                |> List.mapi (fun _ name -> { QuantumCliqueSolver.Vertex.Id = name; Weight = 1.0 })
            Edges = edges
        }
        
        match QuantumCliqueSolver.solve backend cliqueProblem problem.Shots with
        | Error err -> Error err
        | Ok solution ->
            let members = solution.CliqueVertices |> List.map (fun v -> v.Id)
            let strength = calculateStrength members problem.Connections
            let internalConns =
                let memberSet = Set.ofList members
                problem.Connections
                |> List.filter (fun conn ->
                    memberSet.Contains(conn.Person1) && memberSet.Contains(conn.Person2))
                |> List.length
            
            if members.IsEmpty then
                Ok []
            else
                Ok [{
                    Members = members
                    Strength = strength
                    InternalConnections = internalConns
                }]
    
    /// Find the minimum monitor set covering all connections using QAOA min vertex cover
    let private findMonitorSetQaoa (backend: IQuantumBackend) (problem: SocialNetworkProblem) : QuantumResult<PersonId list> =
        let personIndex = createPersonIndex problem.People
        let edges =
            problem.Connections
            |> List.choose (fun conn ->
                match Map.tryFind conn.Person1 personIndex, Map.tryFind conn.Person2 personIndex with
                | Some idx1, Some idx2 -> Some (idx1, idx2)
                | _ -> None)
        
        if edges.IsEmpty then
            Ok []  // No connections to cover
        else
            let vcProblem : QuantumVertexCoverSolver.Problem = {
                Vertices =
                    problem.People
                    |> List.mapi (fun _ name -> { QuantumVertexCoverSolver.Vertex.Id = name; Weight = 1.0 })
                Edges = edges
            }
            
            match QuantumVertexCoverSolver.solve backend vcProblem problem.Shots with
            | Error err -> Error err
            | Ok solution ->
                Ok (solution.CoverVertices |> List.map (fun v -> v.Id))
    
    /// Find optimal 1:1 pairings using QAOA max weight matching
    let private findPairingsQaoa (backend: IQuantumBackend) (problem: SocialNetworkProblem) : QuantumResult<Pairing list> =
        let personIndex = createPersonIndex problem.People
        let edges =
            problem.Connections
            |> List.choose (fun conn ->
                match Map.tryFind conn.Person1 personIndex, Map.tryFind conn.Person2 personIndex with
                | Some idx1, Some idx2 ->
                    Some ({ Source = idx1; Target = idx2; Weight = 1.0 } : QuantumMatchingSolver.Edge)
                | _ -> None)
        
        if edges.IsEmpty then
            Ok []  // No connections to pair
        else
            let matchingProblem : QuantumMatchingSolver.Problem = {
                NumVertices = problem.People.Length
                Edges = edges
            }
            
            match QuantumMatchingSolver.solve backend matchingProblem problem.Shots with
            | Error err -> Error err
            | Ok solution ->
                let pairings =
                    solution.SelectedEdges
                    |> List.map (fun edge ->
                        {
                            Person1 = problem.People.[edge.Source]
                            Person2 = problem.People.[edge.Target]
                            Weight = edge.Weight
                        })
                Ok pairings
    
    // ========================================================================
    // EMPTY RESULT HELPER
    // ========================================================================
    
    /// Create an empty result with the given message
    let private emptyResult (problem: SocialNetworkProblem) (message: string) : SocialNetworkResult =
        {
            Communities = []
            MonitorSet = []
            Pairings = []
            TotalPeople = problem.People.Length
            TotalConnections = problem.Connections.Length
            Message = message
        }
    
    // ========================================================================
    // SOLVE — MAIN DISPATCH
    // ========================================================================
    
    /// Execute social network analysis
    let solve (problem: SocialNetworkProblem) : QuantumResult<SocialNetworkResult> =
        if problem.People.IsEmpty then
            Error (QuantumError.ValidationError ("People", "network must have at least one person"))
        elif problem.People.Length > 100 then
            Error (QuantumError.ValidationError ("People", $"network too large ({problem.People.Length}), maximum is 100"))
        else
            match problem.Backend with
            | None ->
                Error (QuantumError.NotImplemented (
                    "Classical community detection",
                    Some "Provide a quantum backend via SocialNetworkProblem.Backend."))
            | Some backend ->
                // Determine the effective analysis mode
                let effectiveMode =
                    match problem.Mode with
                    | Some mode -> mode
                    | None ->
                        // Legacy fallback: use FindCommunities if MinCommunitySize is set
                        match problem.MinCommunitySize with
                        | Some size -> FindCommunities size
                        | None -> FindLargestCommunity  // Default to max clique
                
                match effectiveMode with
                | FindCommunities minSize ->
                    // Legacy Grover-based path
                    let legacyProblem = { problem with MinCommunitySize = Some minSize }
                    match findCommunitiesQuantum backend legacyProblem with
                    | Ok communities ->
                        Ok {
                            Communities = communities
                            MonitorSet = []
                            Pairings = []
                            TotalPeople = problem.People.Length
                            TotalConnections = problem.Connections.Length
                            Message = 
                                if communities.IsEmpty then
                                    "No communities found with the specified criteria"
                                else
                                    $"Found {communities.Length} communities"
                        }
                    | Error e -> Error e
                
                | FindLargestCommunity ->
                    match findLargestCommunityQaoa backend problem with
                    | Ok communities ->
                        Ok {
                            Communities = communities
                            MonitorSet = []
                            Pairings = []
                            TotalPeople = problem.People.Length
                            TotalConnections = problem.Connections.Length
                            Message =
                                match communities with
                                | [] -> "No community found in the network"
                                | [c] -> $"Found largest community of {c.Members.Length} people"
                                | cs -> $"Found {cs.Length} communities"
                        }
                    | Error e -> Error e
                
                | FindMonitorSet ->
                    match findMonitorSetQaoa backend problem with
                    | Ok monitors ->
                        Ok {
                            Communities = []
                            MonitorSet = monitors
                            Pairings = []
                            TotalPeople = problem.People.Length
                            TotalConnections = problem.Connections.Length
                            Message =
                                if monitors.IsEmpty then
                                    "No monitors needed (no connections in network)"
                                else
                                    $"Found monitor set of {monitors.Length} people covering all connections"
                        }
                    | Error e -> Error e
                
                | FindPairings ->
                    match findPairingsQaoa backend problem with
                    | Ok pairings ->
                        Ok {
                            Communities = []
                            MonitorSet = []
                            Pairings = pairings
                            TotalPeople = problem.People.Length
                            TotalConnections = problem.Connections.Length
                            Message =
                                if pairings.IsEmpty then
                                    "No pairings found (no connections in network)"
                                else
                                    $"Found {pairings.Length} optimal pairings"
                        }
                    | Error e -> Error e
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// <summary>
    /// Fluent builder for social network analysis.
    /// </summary>
    /// <remarks>
    /// Provides an enterprise-friendly API for analyzing social networks
    /// without requiring knowledge of quantum algorithms.
    /// 
    /// **Supported Analysis Modes:**
    /// - `findCommunities N` — Find cliques of exactly size N (Grover's search)
    /// - `findLargestCommunity` — Find the maximum clique (QAOA optimization)
    /// - `findMonitorSet` — Find minimum people covering all connections (QAOA vertex cover)
    /// - `findPairings` — Find optimal 1:1 pairings (QAOA matching)
    /// 
    /// **Example - Finding the Largest Community:**
    /// ```fsharp
    /// let analysis = socialNetwork {
    ///     people ["Alice"; "Bob"; "Carol"; "Dave"]
    ///     connection "Alice" "Bob"
    ///     connection "Bob" "Carol"
    ///     connection "Carol" "Alice"
    ///     connection "Dave" "Alice"
    ///     findLargestCommunity
    ///     backend (LocalBackend.LocalBackend() :> IQuantumBackend)
    /// }
    /// ```
    /// 
    /// **Example - Finding Monitor Set:**
    /// ```fsharp
    /// let monitors = socialNetwork {
    ///     people ["Alice"; "Bob"; "Carol"]
    ///     connection "Alice" "Bob"
    ///     connection "Bob" "Carol"
    ///     findMonitorSet
    ///     backend myBackend
    /// }
    /// ```
    /// </remarks>
    type SocialNetworkBuilder() =
        
        /// Default empty network
        let defaultProblem = {
            People = []
            Connections = []
            MinCommunitySize = None
            Mode = None
            Backend = None
            Shots = 1000
        }
        
        /// Initialize builder
        member _.Yield(_) = defaultProblem
        
        /// Delay execution for computation expressions
        member _.Delay(f: unit -> SocialNetworkProblem) = f
        
        /// Execute the analysis and return result
        member _.Run(f: unit -> SocialNetworkProblem) : QuantumResult<SocialNetworkResult> =
            let problem = f()
            solve problem
        
        /// Combine operations (later operation takes precedence)
        member _.Combine(p1: SocialNetworkProblem, p2: SocialNetworkProblem) = p2
        
        /// Empty expression
        member _.Zero() = defaultProblem
        
        /// <summary>Add a person to the network.</summary>
        /// <param name="personId">Unique identifier for the person</param>
        [<CustomOperation("person")>]
        member _.Person(problem: SocialNetworkProblem, personId: PersonId) : SocialNetworkProblem =
            { problem with People = personId :: problem.People }
        
        /// <summary>Add multiple people to the network.</summary>
        /// <param name="people">List of person identifiers</param>
        [<CustomOperation("people")>]
        member _.People(problem: SocialNetworkProblem, people: PersonId list) : SocialNetworkProblem =
            { problem with People = people @ problem.People }
        
        /// <summary>Add a connection between two people.</summary>
        /// <param name="person1">First person</param>
        /// <param name="person2">Second person</param>
        [<CustomOperation("connection")>]
        member _.Connection(problem: SocialNetworkProblem, person1: PersonId, person2: PersonId) : SocialNetworkProblem =
            let conn = { Person1 = person1; Person2 = person2 }
            { problem with Connections = conn :: problem.Connections }
        
        /// <summary>Add multiple connections at once.</summary>
        /// <param name="connections">List of (person1, person2) tuples</param>
        [<CustomOperation("connections")>]
        member _.Connections(problem: SocialNetworkProblem, connections: (PersonId * PersonId) list) : SocialNetworkProblem =
            let newConns = connections |> List.map (fun (p1, p2) -> { Person1 = p1; Person2 = p2 })
            { problem with Connections = newConns @ problem.Connections }
        
        /// <summary>Find communities of a specific size (Grover's search).</summary>
        /// <param name="size">Exact community size to find</param>
        /// <remarks>
        /// Uses Grover's algorithm with clique oracle for quadratic speedup.
        /// Finds cliques of exactly the specified size.
        /// </remarks>
        [<CustomOperation("findCommunities")>]
        member _.FindCommunities(problem: SocialNetworkProblem, size: int) : SocialNetworkProblem =
            { problem with MinCommunitySize = Some size; Mode = Some (FindCommunities size) }
        
        /// <summary>Find the largest fully-connected group (QAOA optimization).</summary>
        /// <remarks>
        /// Uses QAOA max clique optimization to find the largest community.
        /// Unlike `findCommunities`, this does not require specifying a size —
        /// it finds the maximum clique automatically.
        /// 
        /// Business use: "Who is the biggest tight-knit group in the network?"
        /// </remarks>
        [<CustomOperation("findLargestCommunity")>]
        member _.FindLargestCommunity(problem: SocialNetworkProblem) : SocialNetworkProblem =
            { problem with Mode = Some FindLargestCommunity }
        
        /// <summary>Find the minimum set of people covering all connections (QAOA vertex cover).</summary>
        /// <remarks>
        /// Uses QAOA vertex cover optimization to find the smallest set of people
        /// such that every connection in the network has at least one endpoint in the set.
        /// 
        /// Business use: "What is the minimum number of people we need to monitor
        /// to see all communications in the network?"
        /// </remarks>
        [<CustomOperation("findMonitorSet")>]
        member _.FindMonitorSet(problem: SocialNetworkProblem) : SocialNetworkProblem =
            { problem with Mode = Some FindMonitorSet }
        
        /// <summary>Find optimal 1:1 pairings in the network (QAOA matching).</summary>
        /// <remarks>
        /// Uses QAOA max weight matching to find optimal 1:1 pairings between
        /// connected people. Each person appears in at most one pairing.
        /// 
        /// Business use: "Find the best mentor-mentee pairings" or
        /// "Match project partners optimally."
        /// </remarks>
        [<CustomOperation("findPairings")>]
        member _.FindPairings(problem: SocialNetworkProblem) : SocialNetworkProblem =
            { problem with Mode = Some FindPairings }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance (enables quantum acceleration)</param>
        /// <remarks>
        /// Required for all analysis modes. Supports LocalBackend for simulation
        /// and cloud backends (IonQ, Rigetti, Quantinuum) for hardware execution.
        /// </remarks>
        [<CustomOperation("backend")>]
        member _.Backend(problem: SocialNetworkProblem, backend: IQuantumBackend) : SocialNetworkProblem =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements (default: 1000)</param>
        /// <remarks>
        /// Higher shot counts increase accuracy but take longer to execute.
        /// Recommended: 1000-10000 for production, 100-1000 for testing.
        /// </remarks>
        [<CustomOperation("shots")>]
        member _.Shots(problem: SocialNetworkProblem, shots: int) : SocialNetworkProblem =
            { problem with Shots = shots }
    
    /// <summary>
    /// Create a social network analysis builder.
    /// </summary>
    /// <remarks>
    /// Use this builder to analyze social networks, find communities,
    /// identify influential groups, monitor connections, and find optimal pairings.
    /// 
    /// **Business Applications:**
    /// - **Marketing**: Identify influencer groups for targeted campaigns
    /// - **Security**: Detect fraud rings through connection patterns
    /// - **HR**: Analyze team collaboration and communication networks
    /// - **Product**: Find user segments for feature recommendations
    /// - **Compliance**: Find minimum observer set for network monitoring
    /// - **Mentoring**: Match mentors to mentees optimally
    /// </remarks>
    let socialNetwork = SocialNetworkBuilder()
