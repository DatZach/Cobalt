"use strict";
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
const path = __importStar(require("path"));
const vscode = __importStar(require("vscode"));
const process = __importStar(require("child_process"));
async function activate(context) {
    context.subscriptions.push(vscode.commands.registerCommand('extension.cobalt-debug.runEditorContents', (resource) => launchDebugger(resource, false)), vscode.commands.registerCommand('extension.cobalt-debug.debugEditorContents', (resource) => launchDebugger(resource, true)));
    context.subscriptions.push(vscode.debug.registerDebugConfigurationProvider('cobalt', new CobaltDebugConfigurationProvider()));
    context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('cobalt', new CobaltDebugAdapterFactory()));
}
function deactivate() {
    return undefined;
}
function launchDebugger(resource, attach) {
    if (resource == null && vscode.window.activeTextEditor != null)
        resource = vscode.window.activeTextEditor.document.uri;
    if (resource == null)
        return;
    vscode.debug.startDebugging(vscode.workspace.getWorkspaceFolder(resource), {
        type: 'cobalt',
        name: 'Debug',
        request: 'launch',
        entryCobPath: resource.fsPath,
        commandLine: []
    }, {
        noDebug: !attach
    });
}
class CobaltDebugConfigurationProvider {
    resolveDebugConfiguration(folder, config, token) {
        if (config.type == null
            && config.request == null
            && config.name == null) {
            const editor = vscode.window.activeTextEditor;
            if (editor != null && editor.document.languageId === 'cob') {
                config.type = 'cobalt';
                config.name = 'Launch';
                config.request = 'launch';
                config.entryCobPath = '${file}';
                config.commandLine = [];
            }
        }
        if (config.entryCobPath == null) {
            return vscode.window.showInformationMessage("Cannot find a program to debug")
                .then(_ => undefined);
        }
        return config;
    }
}
class CobaltDebugAdapterFactory {
    server;
    stdout;
    async createDebugAdapterDescriptor(_session) {
        const config = vscode.workspace.getConfiguration("cobalt");
        const serverDirectory = config.get("toolchainDirectory");
        if (serverDirectory == null) {
            return vscode.window.showErrorMessage("Cobalt Toolchain Directory not specified")
                .then(_ => undefined);
        }
        const serverPath = path.join(serverDirectory, 'Compiler.exe');
        const pipeName = "eb925160-12d5-485a-8b99-888865cb4ac3"; // For debugging
        //const pipeName = crypto.randomUUID();
        const pipePath = path.join('\\\\.\\pipe\\', pipeName);
        this.server = process.spawn(serverPath, [_session.configuration.entryCobPath, "--debug=" + pipeName, ..._session.configuration.commandLine], {
            cwd: _session.workspaceFolder?.uri.fsPath
        });
        this.stdout = this.stdout || vscode.window.createOutputChannel("Cobalt Application");
        this.stdout.clear();
        this.stdout.show();
        this.server.stdout?.on('data', (chunk) => {
            this.stdout?.append(chunk.toString());
        });
        // TODO HACK Need to find a better way to wait for the pipe to open on the server
        await new Promise(x => setTimeout(x, 150));
        return new vscode.DebugAdapterNamedPipeServer(pipePath);
    }
    dispose() {
        this.server?.kill();
    }
}
//# sourceMappingURL=dap-client.js.map