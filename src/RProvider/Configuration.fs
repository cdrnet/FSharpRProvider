﻿/// [omit]
module RProvider.Internal.Configuration

open System
open System.IO
open System.Reflection
open System.Configuration
open System.Collections.Generic

/// Returns the Assembly object of RProvider.Runtime.dll (this needs to
/// work when called from RProvider.DesignTime.dll and also RProvider.Server.exe)
let getRProviderRuntimeAssembly() =
  AppDomain.CurrentDomain.GetAssemblies()
  |> Seq.find (fun a -> a.FullName.StartsWith("RProvider.Runtime,"))

/// Finds directories relative to 'dirs' using the specified 'patterns'.
/// Patterns is a string, such as "..\foo\*\bar" split by '\'. Standard
/// .NET libraries do not support "*", so we have to do it ourselves..
let rec searchDirectories patterns dirs = 
  match patterns with 
  | [] -> dirs
  | "*"::patterns ->
      dirs |> List.collect (Directory.GetDirectories >> List.ofSeq)
      |> searchDirectories patterns
  | name::patterns -> 
      dirs |> List.map (fun d -> Path.Combine(d, name))
      |> searchDirectories patterns

/// Reads the 'RProvider.dll.config' file and gets the 'ProbingLocations' 
/// parameter from the configuration file. Resolves the directories and returns
/// them as a list.
let getProbingLocations() = 
  try
    let root = getRProviderRuntimeAssembly().Location
    let config = System.Configuration.ConfigurationManager.OpenExeConfiguration(root)
    let pattern = config.AppSettings.Settings.["ProbingLocations"]
    if pattern <> null then
      [ let pattern = pattern.Value.Split(';', ',') |> List.ofSeq
        for pat in pattern do 
          let roots = [ Path.GetDirectoryName(root) ]
          for dir in roots |> searchDirectories (List.ofSeq (pat.Split('/','\\'))) do
            if Directory.Exists(dir) then yield dir ]
    else []
  with :? ConfigurationErrorsException | :? KeyNotFoundException -> []


/// Given an assembly name, try to find it in either assemblies
/// loaded in the current AppDomain, or in one of the specified 
/// probing directories.
let resolveReferencedAssembly (asmName:string) = 
  
  // First, try to find the assembly in the currently loaded assemblies
  let fullName = AssemblyName(asmName)
  let loadedAsm = 
    System.AppDomain.CurrentDomain.GetAssemblies()
    |> Seq.tryFind (fun a -> AssemblyName.ReferenceMatchesDefinition(fullName, a.GetName()))
  match loadedAsm with
  | Some asm -> asm
  | None ->

    // Otherwise, search the probing locations for a DLL file
    let libraryName = 
      let idx = asmName.IndexOf(',') 
      if idx > 0 then asmName.Substring(0, idx) else asmName

    let asm =
      getProbingLocations()
      |> Seq.tryPick (fun dir ->
          let library = Path.Combine(dir, libraryName+".dll")
          if File.Exists(library) then 
            let asm = Assembly.LoadFrom(library)
            if asm.FullName = asmName then Some(asm) else None
          else None)
             
    defaultArg asm null
