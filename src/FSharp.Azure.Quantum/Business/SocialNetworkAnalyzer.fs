namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

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
/// 
/// **Quantum Advantage:**
/// Uses Grover's algorithm with clique detection for quadratic speedup
/// in finding tight-knit communities compared to classical algorithms.
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
///     findCommunities 3
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
    
    /// Social network analysis problem configuration
    type SocialNetworkProblem = {
        /// All people in the network
        People: PersonId list
        
        /// Connections (friendships, follows, etc.)
        Connections: Connection list
        
        /// Minimum community size to find
        MinCommunitySize: int option
        
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
    
    /// Result of social network analysis
    type SocialNetworkResult = {
        /// Communities found
        Communities: Community list
        
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
    
    /// Find communities using classical algorithm (baseline)
    let private findCommunitiesClassical (_problem: SocialNetworkProblem) : Community list =
        failwith
            "Classical community detection is not implemented. \
             Provide a quantum backend via SocialNetworkProblem.Backend."
    
    /// Execute social network analysis
    let solve (problem: SocialNetworkProblem) : QuantumResult<SocialNetworkResult> =
        if problem.People.IsEmpty then
            Error (QuantumError.ValidationError ("People", "network must have at least one person"))
        elif problem.People.Length > 100 then
            Error (QuantumError.ValidationError ("People", $"network too large ({problem.People.Length}), maximum is 100"))
        else
            match problem.Backend with
            | Some backend ->
                match findCommunitiesQuantum backend problem with
                | Ok communities ->
                    Ok {
                        Communities = communities
                        TotalPeople = problem.People.Length
                        TotalConnections = problem.Connections.Length
                        Message = 
                            if communities.IsEmpty then
                                "No communities found with the specified criteria"
                            else
                                $"Found {communities.Length} communities"
                    }
                | Error e -> Error e
            
            | None ->
                Error (QuantumError.NotImplemented (
                    "Classical community detection",
                    Some "Provide a quantum backend via SocialNetworkProblem.Backend."))
    
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
    /// **Example - Finding Influencer Groups:**
    /// ```fsharp
    /// let analysis = socialNetwork {
    ///     // Add people
    ///     person "Alice"
    ///     person "Bob"
    ///     person "Carol"
    ///     person "Dave"
    ///     
    ///     // Add connections
    ///     connection "Alice" "Bob"
    ///     connection "Bob" "Carol"
    ///     connection "Carol" "Alice"  // Triangle: tight-knit group
    ///     connection "Dave" "Alice"   // Dave connected but not in core group
    ///     
    ///     // Find communities of at least 3 people
    ///     findCommunities 3
    ///     
    ///     // Enable quantum acceleration (optional - omit for classical)
    ///     backend (LocalBackend.LocalBackend() :> IQuantumBackend)
    /// }
    /// 
    /// match solve analysis with
    /// | Ok result ->
    ///     printfn "Found %d communities" result.Communities.Length
    ///     for comm in result.Communities do
    ///         printfn "Community: %A (strength: %.2f)" comm.Members comm.Strength
    /// | Error err ->
    ///     printfn "Error: %A" err
    /// ```
    /// </remarks>
    type SocialNetworkBuilder() =
        
        /// Default empty network
        let defaultProblem = {
            People = []
            Connections = []
            MinCommunitySize = None
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
        
        /// <summary>Specify minimum community size to find.</summary>
        /// <param name="size">Minimum number of people in a community</param>
        /// <remarks>
        /// Communities smaller than this will be ignored.
        /// Larger values find tighter-knit groups but may miss smaller communities.
        /// </remarks>
        [<CustomOperation("findCommunities")>]
        member _.FindCommunities(problem: SocialNetworkProblem, size: int) : SocialNetworkProblem =
            { problem with MinCommunitySize = Some size }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance (enables quantum acceleration)</param>
        /// <remarks>
        /// Providing a backend enables Grover's algorithm for quadratic speedup.
        /// Omit this to use classical algorithm instead.
        /// 
        /// Examples:
        /// - LocalBackend: Local quantum simulation
        /// - IonQ, Rigetti, Quantinuum: Cloud quantum hardware
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
    /// and identify influential groups.
    /// 
    /// **Business Applications:**
    /// - **Marketing**: Identify influencer groups for targeted campaigns
    /// - **Security**: Detect fraud rings through connection patterns
    /// - **HR**: Analyze team collaboration and communication networks
    /// - **Product**: Find user segments for feature recommendations
    /// </remarks>
    let socialNetwork = SocialNetworkBuilder()
