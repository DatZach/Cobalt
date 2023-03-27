# Cobalt
Cobalt is a research/hobby computing environment. More than an Operating System, Cobalt aims to implement every
element of a personal computer. Including hardware and software stacks. Inspiration from existing system is unavoidable,
but where I see something interesting to try (or even better, simplify) Cobalt will experiment with. To clarify this
means that CPU, Compilers, Kernels, Shells, and Programming Languages will all be home rolled to this environment.
This does ultimately limit the portability of software to and from Cobalt, but that's fine as these are not in scope regardless.
An ultimate goal of Cobalt would be self-hosted in addition to running an HTTP server to a simple website which a
non-Cobalt host can access via standard browser. The standards here are perhaps the only ones which will not be
unique to Cobalt (out of nessessity).

# Cobalt OS
The goals here would be to implement an Operating System following the previously mentioned guidelines. An additional
goal of supporting both Cobalt and x86-64 architectures in mind. The reasoning behind this is that the hardware layer will
take a while to develop and I know most of the limitations of the Cobalt hardware and can design around it. It is likely
that the first iteration x86 Cobalt OS will change significantly to support Cobalt architecture.

# Highlevel Goals
 - 16bit (Cobalt) and 64bit (x86-64) compatibility + multithreading
 - VGA Shell with rich cli and windowing, I would like to investigate some novel implementations here
 - Hardware Supported: Disk Drives, Keyboard, VGA, Sound
 - Unique filesystem capable of flash and magnetic storage, 640KB to 4TB capacity
 - Unique executable format, simple simple simple. Support for library code and native entry points
 - Unique programming language. Kernel and programs will be written in this
 - Fully self hosted! Including compiler and OS
 - Standard Software: CLI Shell, Text/Code Editor, Compiler, Assembler
 
# Out of scope for x86-64 build
 - Assembler
 - ASCII/Unicode standard (A new encoding doesn't give us any benefits and we'll have a hard time networking)
