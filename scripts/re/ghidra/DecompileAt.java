// killer-sense - script headless do Ghidra (somente leitura): descompila as funcoes
// nos enderecos dados e, opcionalmente, as funcoes chamadas por elas (1 nivel).
// Uso: -postScript DecompileAt.java <saida.txt> <callees:0|1> <endereco1> [endereco2...]
// @category killer-sense

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.LinkedHashSet;
import java.util.Set;

public class DecompileAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 3) { printerr("uso: <saida.txt> <callees:0|1> <endereco>..."); return; }
        boolean callees = args[1].equals("1");
        Set<Function> funcs = new LinkedHashSet<>();
        for (int i = 2; i < args.length; i++) {
            Function f = getFunctionAt(toAddr(args[i]));
            if (f == null) f = getFunctionContaining(toAddr(args[i]));
            if (f == null) { println("sem funcao em " + args[i]); continue; }
            funcs.add(f);
            if (callees) funcs.addAll(f.getCalledFunctions(monitor));
        }
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        try (PrintWriter out = new PrintWriter(new FileWriter(args[0]))) {
            for (Function f : funcs) {
                if (f.isThunk() || f.isExternal()) continue;
                out.println("\n// ===== " + f.getName() + " @ " + f.getEntryPoint());
                DecompileResults res = dec.decompileFunction(f, 120, monitor);
                out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "// falha: " + res.getErrorMessage());
            }
        } finally {
            dec.dispose();
        }
        println("funcoes gravadas: " + funcs.size());
    }
}
