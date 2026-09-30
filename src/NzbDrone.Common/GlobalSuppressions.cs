// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S6549", Justification = "Core disk provider and filesystem abstractions.")]
[assembly: SuppressMessage("Security", "S6549", Justification = "Core disk provider and filesystem abstractions.")]
