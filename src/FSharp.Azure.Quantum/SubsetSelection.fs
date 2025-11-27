namespace FSharp.Azure.Quantum

/// Generic Subset Selection Framework for Knapsack, Subset Sum, Portfolio Optimization, and Set Cover problems.
/// 
/// Provides fluent builder API for composing subset selection problems with multi-dimensional weights,
/// constraints, and objectives. Supports both quantum (QUBO) and classical (DP, greedy) solvers.
module SubsetSelection =
    
    // ============================================================================
    // CORE TYPES - Domain Model
    // ============================================================================
    
    /// Represents an item with multi-dimensional weights for subset selection.
    type Item<'T when 'T : equality> = {
        /// Unique item identifier
        Id: string
        
        /// Item value (business data)
        Value: 'T
        
        /// Multi-dimensional weights (e.g., "weight" -> 3.0, "value" -> 1000.0)
        Weights: Map<string, float>
        
        /// Custom metadata for domain-specific data
        Metadata: Map<string, obj>
    }
    
    // ============================================================================
    // HELPER FUNCTIONS - Item Creation
    // ============================================================================
    
    /// Create an item with multi-dimensional weights.
    /// Example: itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
    let itemMulti (id: string) (value: 'T) (weights: (string * float) list) : Item<'T> =
        {
            Id = id
            Value = value
            Weights = Map.ofList weights
            Metadata = Map.empty
        }
    
    /// Create an item with a single dimension.
    /// Example: item "item1" 5.0 "value" 5.0
    let item (id: string) (value: 'T) (dimension: string) (weight: float) : Item<'T> =
        itemMulti id value [dimension, weight]
    
    /// Create a numeric item (value is also the single weight).
    /// Example: numericItem "num1" 5.0
    let numericItem (id: string) (value: float) : Item<float> =
        item id value "value" value
