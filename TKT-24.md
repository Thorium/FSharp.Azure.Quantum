# TKT-24: Implement: Domain Builder - TSP

## Status
**State:** In Progress


## Description
Implement high-level TSP API with TSP.solve and TSP.createProblem that abstracts QUBO encoding.

---

**Comment (2025-11-23 19:59:30)**: # Implement: Domain Builder - TSP

**Objective**
Implement high-level TSP (Traveling Salesman Problem) API with TSP.solve and TSP.createProblem that abstracts QUBO encoding and provides intuitive F# interface for users.

**Context**
Users should not need to understand QUBO encoding or quantum circuits to solve TSP problems. This domain builder provides a simple API where users specify cities and distances, and receive tour solutions with ordered cities and total distance.

**Domain Implementation Rule**
⚠️ **CRITICAL**: Implement ALL TSP domain builder code in a SINGLE FILE `TspBuilder.fs`.

* Include: TSP types (City, Tour), problem creation, QUBO encoding integration, solution formatting - ALL in one file
* **Ignore F# conventions** that separate types and functions into different files
* **Context window optimization**: AI needs all TSP domain code visible simultaneously

**Task**
Implement TSP domain builder following TDD methodology:

* Write tests for TSP.createProblem with various city counts
* Implement TSP.solve function (classical and quantum variants)
* Create TSP-specific types (City, Tour, Distance)
* Build distance matrix computation from coordinates
* Integrate QUBO encoding for TSP
* Format solutions as Tour with ordered cities and total distance
* Validate tour correctness (visits all cities exactly once)

**Implementation Approach**

* Follow test-driven development with comprehensive test coverage
* Hide QUBO complexity from users
* Provide clear, domain-specific types
* **Keep all TSP domain code in single file for AI context optimization**

**Technical Requirements**

* API: TSP.solve(cities, ?backend, ?parameters)
* Input: List of cities with coordinates or distance matrix
* Output: Tour record with ordered cities and total distance
* Distance calculation: Euclidean or custom distance matrix
* QUBO integration: Convert TSP to QUBO format internally
* Solution validation: Ensure valid tours (all cities visited once)

**Definition of Done**

* Tests written and passing for all functionality
* TSP.solve working for classical and quantum backends
* TSP.createProblem correctly structures problem
* Distance matrix computation accurate
* QUBO encoding integration working
* Tour solutions correctly formatted
* **ALL TSP domain code consolidated in single file TspBuilder.fs**
* Code review completed and approved
* TSP solutions validated for correctness and tour validity

**Labels**
backend, implementation, domain-builder, tsp


## Metadata
- **Ticket ID:** d0e1f2a3-4b5c-6d7e-8f9a-0b1c2d3e4f5a

---
_Assigned via WorkflowAutomation_
_Full ticket details are included in this file for your reference._
