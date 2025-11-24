# TKT-25: Implement: Domain Builder - Portfolio

## Status
**State:** In Progress


## Description
Implement high-level Portfolio optimization API with Portfolio.solve that abstracts QUBO encoding.

---

**Comment (2025-11-23 19:59:30)**: # Implement: Domain Builder - Portfolio

**Objective**
Implement high-level Portfolio optimization API with Portfolio.solve and Portfolio.createProblem that abstracts QUBO encoding and provides intuitive interface for portfolio selection problems.

**Context**
Users need to solve portfolio optimization problems without understanding QUBO encoding or quantum circuits. This domain builder provides a simple API where users specify assets, returns, risk metrics, and constraints, then receive optimal portfolio allocations.

**Domain Implementation Rule**
⚠️ **CRITICAL**: Implement ALL Portfolio domain builder code in a SINGLE FILE `PortfolioBuilder.fs`.

* Include: Portfolio types (Asset, Portfolio, Constraints), problem creation, QUBO encoding integration, solution formatting - ALL in one file
* **Ignore F# conventions** that separate types and functions into different files
* **Context window optimization**: AI needs all portfolio domain code visible simultaneously

**Task**
Implement Portfolio domain builder following TDD methodology:

* Write tests for Portfolio.createProblem with various asset counts
* Implement Portfolio.solve function (classical and quantum variants)
* Create Portfolio-specific types (Asset, Portfolio, Constraints)
* Build constraint handling (budget, diversification, exposure limits)
* Integrate QUBO encoding for portfolio optimization
* Format solutions as PortfolioSolution with selected assets and allocations
* Validate portfolio correctness (constraint satisfaction)

**Implementation Approach**

* Follow test-driven development with comprehensive test coverage
* Hide QUBO complexity from users
* Provide clear, domain-specific types
* **Keep all portfolio domain code in single file for AI context optimization**

**Technical Requirements**

* API: Portfolio.solve(assets, returns, covariance, constraints, ?backend)
* Input: Assets with returns, covariance matrix, budget/diversification constraints
* Output: PortfolioSolution with selected assets, allocations, expected return, risk
* Constraints: Budget limits, sector diversification, exposure limits
* QUBO integration: Convert portfolio optimization to QUBO format internally
* Solution validation: Ensure valid portfolios (constraints satisfied)

**Definition of Done**

* Tests written and passing for all functionality
* Portfolio.solve working for classical and quantum backends
* Portfolio.createProblem correctly structures problem
* Constraint handling working (budget, diversification, exposure)
* QUBO encoding integration working
* Portfolio solutions correctly formatted with metrics
* **ALL Portfolio domain code consolidated in single file PortfolioBuilder.fs**
* Code review completed and approved
* Portfolio solutions validated for correctness and constraint satisfaction

**Labels**
backend, implementation, domain-builder, portfolio


## Metadata
- **Ticket ID:** e1f2a3b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b

---
_Assigned via WorkflowAutomation_
_Full ticket details are included in this file for your reference._
