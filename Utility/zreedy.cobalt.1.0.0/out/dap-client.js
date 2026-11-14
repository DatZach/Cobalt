'use strict';
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(require("vscode"));
// import { MockDebugSession } from './mockDebug';
// import { FileAccessor } from './mockRuntime';
async function activate(context) {
    context.subscriptions.push(vscode.commands.registerCommand('extension.cobalt-debug.runEditorContents', (resource) => {
        let targetResource = resource;
        if (!targetResource && vscode.window.activeTextEditor) {
            targetResource = vscode.window.activeTextEditor.document.uri;
        }
        if (targetResource) {
            // TODO Probably should unify collecting this?
            const config = vscode.workspace.getConfiguration("cobalt");
            let serverDirectory = config.get("toolchainDirectory") || "X:\\Cobalt\\Compiler\\bin\\Debug\\net6.0";
            let uhm = serverDirectory + "\\Compiler.exe " + targetResource.fsPath;
            //proc.spawn(serverDirectory + "\\Compiler.exe", [ targetResource.fsPath ]);
            //proc.exec(uhm);
            let terminal = vscode.window.createTerminal("Application");
            terminal.show();
            terminal.sendText(uhm);
            // TODO Use this later
            // vscode.debug.startDebugging(undefined, {
            // 	type: 'cobalt',
            // 	name: 'Run File',
            // 	request: 'launch',
            // 	program: targetResource.fsPath
            // }, {
            //     noDebug: true
            // });
        }
    }), vscode.commands.registerCommand('extension.cobalt-debug.debugEditorContents', (resource) => {
        let targetResource = resource;
        if (!targetResource && vscode.window.activeTextEditor) {
            targetResource = vscode.window.activeTextEditor.document.uri;
        }
        if (targetResource) {
            vscode.debug.startDebugging(undefined, {
                type: 'cobalt',
                name: 'Debug File',
                request: 'launch',
                program: targetResource.fsPath,
                stopOnEntry: true
            });
        }
    }));
    // register a configuration provider for 'mock' debug type
    // const provider = new MockConfigurationProvider();
    // context.subscriptions.push(vscode.debug.registerDebugConfigurationProvider('cobalt', provider));
    // // register a dynamic configuration provider for 'mock' debug type
    // context.subscriptions.push(vscode.debug.registerDebugConfigurationProvider('cobalt', {
    // 	provideDebugConfigurations(folder: WorkspaceFolder | undefined): ProviderResult<DebugConfiguration[]> {
    // 		return [
    // 			{
    // 				name: "Dynamic Launch",
    // 				request: "launch",
    // 				type: "cobalt",
    // 				program: "${file}"
    // 			}
    // 		];
    // 	}
    // }, vscode.DebugConfigurationProviderTriggerKind.Dynamic));
    // let factory = new InlineDebugAdapterFactory();
    // context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('cobalt', factory));
    // // if ('dispose' in factory) {
    // // 	context.subscriptions.push(factory);
    // // }
    // // // override VS Code's default implementation of the debug hover
    // // // here we match only Mock "variables", that are words starting with an '$'
    // // context.subscriptions.push(vscode.languages.registerEvaluatableExpressionProvider('markdown', {
    // // 	provideEvaluatableExpression(document: vscode.TextDocument, position: vscode.Position): vscode.ProviderResult<vscode.EvaluatableExpression> {
    // // 		const VARIABLE_REGEXP = /\$[a-z][a-z0-9]*/ig;
    // // 		const line = document.lineAt(position.line).text;
    // // 		let m: RegExpExecArray | null;
    // // 		while (m = VARIABLE_REGEXP.exec(line)) {
    // // 			const varRange = new vscode.Range(position.line, m.index, position.line, m.index + m[0].length);
    // // 			if (varRange.contains(position)) {
    // // 				return new vscode.EvaluatableExpression(varRange);
    // // 			}
    // // 		}
    // // 		return undefined;
    // // 	}
    // // }));
    // // // override VS Code's default implementation of the "inline values" feature"
    // // context.subscriptions.push(vscode.languages.registerInlineValuesProvider('markdown', {
    // // 	provideInlineValues(document: vscode.TextDocument, viewport: vscode.Range, context: vscode.InlineValueContext) : vscode.ProviderResult<vscode.InlineValue[]> {
    // // 		const allValues: vscode.InlineValue[] = [];
    // // 		for (let l = viewport.start.line; l <= context.stoppedLocation.end.line; l++) {
    // // 			const line = document.lineAt(l);
    // // 			var regExp = /\$([a-z][a-z0-9]*)/ig;	// variables are words starting with '$'
    // // 			do {
    // // 				var m = regExp.exec(line.text);
    // // 				if (m) {
    // // 					const varName = m[1];
    // // 					const varRange = new vscode.Range(l, m.index, l, m.index + varName.length);
    // // 					// some literal text
    // // 					//allValues.push(new vscode.InlineValueText(varRange, `${varName}: ${viewport.start.line}`));
    // // 					// value found via variable lookup
    // // 					allValues.push(new vscode.InlineValueVariableLookup(varRange, varName, false));
    // // 					// value determined via expression evaluation
    // // 					//allValues.push(new vscode.InlineValueEvaluatableExpression(varRange, varName));
    // // 				}
    // // 			} while (m);
    // // 		}
    // // 		return allValues;
    // // 	}
    // // }));
}
function deactivate() {
    return undefined;
}
//# sourceMappingURL=dap-client.js.map