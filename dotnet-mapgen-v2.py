#!/usr/bin/env python
#
# USAGE:    dotnet-mapgen [-h] {generate,merge} PID
#
# In generate mode, this tool reads the /tmp/perfinfo-PID.map file generated
# by the CLR when running with COMPlus_PerfMapEnabled=1, and finds all load
# events for managed assemblies. For each managed assembly found in this way,
# the tool runs crossgen to generate a symbol mapping file (akin to debuginfo).
#
# In merge mode, this tool finds the load address of each managed assembly in
# the target process, and merges the addresses from the crossgen-generated
# map files into the main /tmp/perf-PID.map file for the process. The crossgen-
# generated map files contain relative addresses, so the tool has to translate
# these into absolute addresses using the load address of each managed assembly
# in the target process.
#
# Copyright (C) 2017, Sasha Goldshtein
# Licensed under the MIT License

import argparse
import glob
import os
import shutil
import subprocess
import tempfile

def bail(error):
    print("ERROR: " + error)
    exit(1)

def get_assembly_list(pid):
    assemblies = [] 
    try:
        with open("/tmp/perfinfo-%d.map" % pid) as f:
            for line in f:
                parts = line.split(';')
                if len(parts) < 2 or parts[0] != "ImageLoad":
                    continue
                assemblies.append(parts[1])
    except IOError:
        bail("error opening /tmp/perfinfo-%d.map file" % pid)
    return assemblies

def find_libcoreclr(pid):
    libcoreclr = subprocess.check_output(
        "cat /proc/%d/maps | grep libcoreclr.so | head -1 | awk '{ print $6 }'"
        % pid, shell=True)
    return libcoreclr

def download_crossgen(libcoreclr):
    # Updated for CoreCLR 2.0. project.json doesn't work anymore, need to generate
    # a .csproj and restore from that. In the meantime, portable RIDs showed up,
    # so we no longer need anything more specific than "linux-x64" (assuming, of
    # course, that we're on Linux x64).
    coreclr_ver = "2.0.0"
    rid = "linux-x64"
    tmp_folder = tempfile.mkdtemp(prefix="crossgen")
    project = os.path.join(tmp_folder, "project.csproj")
    with open(project, "w") as f:
        f.write("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETCore.Runtime.CoreCLR" Version="%s" />
  </ItemGroup>
</Project>\n""" % coreclr_ver)
    subprocess.check_call("dotnet restore %s --packages %s -r %s >/dev/null 2>&1"
                          % (project, tmp_folder, rid), shell=True)
    shutil.copy(
        "%s/runtime.%s.microsoft.netcore.runtime.coreclr/%s/tools/crossgen"
                                        % (tmp_folder, rid, coreclr_ver),
        os.path.dirname(libcoreclr))
    print("crossgen succesfully downloaded and placed in libcoreclr's dir")
    shutil.rmtree(tmp_folder)

def find_crossgen(pid):
    libcoreclr = find_libcoreclr(pid)
    path = os.path.dirname(libcoreclr)
    crossgen = os.path.join(path, "crossgen")
    if not os.path.isfile(crossgen):
        print("couldn't find crossgen, trying to fetch it automatically...")
        download_crossgen(libcoreclr)
    return crossgen

def generate(pid):
    assemblies = get_assembly_list(pid)
    crossgen = find_crossgen(pid)
    asm_list = str.join(":", assemblies)
    succeeded, failed = (0, 0)
    for assembly in assemblies:
        rc = subprocess.call(("%s /Trusted_Platform_Assemblies '%s' " +
                              "/CreatePerfMap /tmp %s >/dev/null 2>&1") %
                             (crossgen, asm_list, assembly), shell=True)
        if rc == 0:
            succeeded += 1
        else:
            failed += 1
            #print ("crossgen failed: %s | %s | %s" % (crossgen, asm_list, assembly))
    print("crossgen map generation: %d succeeded, %d failed" %
          (succeeded, failed))

def get_base_address(pid, assembly):
    hexaddr = subprocess.check_output(
        "cat /proc/%d/maps | grep %s | head -1 | cut -d '-' -f 1" %
        (pid, assembly), shell=True)
    if hexaddr == '':
        return -1
    return int(hexaddr, 16)

def append_perf_map(assembly, asm_map, pid):
    base_address = get_base_address(pid, assembly)
    lines_to_add = ""
    with open(asm_map) as f:
        for line in f:
            parts = line.split()
            offset, size, symbol = parts[0], parts[1], str.join(" ", parts[2:])
            offset = int(offset, 16) + base_address
            lines_to_add += "%016x %s %s\n" % (offset, size, symbol)
    with open("/tmp/perf-%d.map" % pid, "a") as perfmap:
        perfmap.write(lines_to_add)

def merge(pid):
    assemblies = get_assembly_list(pid)
    succeeded, failed = (0, 0)
    for assembly in assemblies:
        # TODO The generated map files have a GUID embedded in them, which
        #      allows multiple versions to coexist (probably). How do we get
        #      this GUID? E.g.:
        #         System.Runtime.ni.{819d412e-d773-4dbb-8d01-20d412b6cf09}.map
        matches = glob.glob("/tmp/%s.ni.{*}.map" %
                            os.path.splitext(os.path.basename(assembly))[0])
        if len(matches) == 0:
            failed += 1
        else:
            append_perf_map(assembly, matches[0], pid)
            succeeded += 1
    print("perfmap merging: %d succeeded, %d failed" % (succeeded, failed))

parser = argparse.ArgumentParser(description=
    "Generates map files for crossgen-compiled assemblies, and merges them " +
    "into the main perf map file. Built for use with .NET Core on Linux.")
parser.add_argument("action", choices=["generate", "merge"],
    help="the action to perform")
parser.add_argument("pid", type=int, help="the dotnet process id")
args = parser.parse_args()

if args.action == "generate":
    generate(args.pid)
elif args.action == "merge":
    merge(args.pid)
