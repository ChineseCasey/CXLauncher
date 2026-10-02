$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class CxShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface Blob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] source, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string name,
        IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry,
        [MarshalAs(UnmanagedType.LPStr)] string target, uint flags, uint flags2, out Blob code, out Blob errors);
    public static void Compile(string sourcePath, string outputPath) {
        byte[] source = Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath));
        Blob code, errors;
        int result = D3DCompile(source, (UIntPtr)source.Length, sourcePath, IntPtr.Zero, IntPtr.Zero, "main", "ps_3_0", 32768, 0, out code, out errors);
        try {
            if (result < 0) throw new InvalidOperationException(errors == null ? "Shader compilation failed." : Marshal.PtrToStringAnsi(errors.GetBufferPointer(), (int)errors.GetBufferSize().ToUInt64()));
            byte[] bytes = new byte[(int)code.GetBufferSize().ToUInt64()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            File.WriteAllBytes(outputPath, bytes);
        } finally { if (code != null) Marshal.ReleaseComObject(code); if (errors != null) Marshal.ReleaseComObject(errors); }
    }
}
'@
$sourcePath = Join-Path $projectRoot 'src/CXLauncher/Assets/Shaders/liquid.hlsl'
$outputPath = Join-Path $projectRoot 'src/CXLauncher/Assets/Shaders/liquid.ps'
[CxShaderCompiler]::Compile($sourcePath, $outputPath)
Write-Output "Compiled ps_3_0: $outputPath ($((Get-Item -LiteralPath $outputPath).Length) bytes)"
