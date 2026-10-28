// killer-sense - script headless do Ghidra (somente leitura do binario analisado).
// Para cada regex passada nos argumentos: acha strings definidas que casam, segue as
// referencias ate as funcoes que as usam e grava o codigo descompilado num arquivo.
// Uso (analyzeHeadless ... -postScript DecompileByString.java <saida.txt> <regex1> [regex2...])
// @category killer-sense

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.data.StringDataInstance;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.regex.Pattern;

public class DecompileByString extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) { printerr("uso: <saida.txt> <regex>..."); return; }
        Pattern[] pats = new Pattern[args.length - 1];
        for (int i = 1; i < args.length; i++) pats[i - 1] = Pattern.compile(args[i]);

        // funcao -> strings que levaram ate ela
        Map<Function, StringBuilder> hits = new LinkedHashMap<>();
        DataIterator it = currentProgram.getListing().getDefinedData(true);
        while (it.hasNext() && !monitor.isCancelled()) {
            Data d = it.next();
            StringDataInstance sdi = StringDataInstance.getStringDataInstance(d);
            if (sdi == StringDataInstance.NULL_INSTANCE) continue;
            String s = sdi.getStringValue();
            if (s == null) continue;
            boolean match = false;
            for (Pattern p : pats) if (p.matcher(s).find()) { match = true; break; }
            if (!match) continue;
            for (Reference r : getReferencesTo(d.getAddress())) {
                Function f = getFunctionContaining(r.getFromAddress());
                if (f == null) continue;
                hits.computeIfAbsent(f, k -> new StringBuilder()).append("//   \"").append(s).append("\"\n");
            }
        }

        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        try (PrintWriter out = new PrintWriter(new FileWriter(args[0]))) {
            out.println("// " + currentProgram.getName() + " - " + hits.size() + " funcoes");
            for (Map.Entry<Function, StringBuilder> e : hits.entrySet()) {
                Function f = e.getKey();
                out.println("\n// ===== " + f.getName() + " @ " + f.getEntryPoint());
                out.print(e.getValue());
                DecompileResults res = dec.decompileFunction(f, 120, monitor);
                out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "// falha: " + res.getErrorMessage());
            }
        } finally {
            dec.dispose();
        }
        println("funcoes gravadas: " + hits.size());
    }
}
