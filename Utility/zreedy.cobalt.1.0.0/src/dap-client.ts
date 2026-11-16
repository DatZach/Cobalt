import * as path from 'path';
import * as vscode from 'vscode';
import * as process from 'child_process';

import {
    WorkspaceFolder,
    DebugConfiguration,
    ProviderResult,
    CancellationToken
} from 'vscode';

export async function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(
		vscode.commands.registerCommand(
            'extension.cobalt-debug.runEditorContents',
            (resource: vscode.Uri) => launchDebugger(resource, false)
        ),
		vscode.commands.registerCommand(
            'extension.cobalt-debug.debugEditorContents',
            (resource: vscode.Uri) => launchDebugger(resource, true)
        )
	);

	context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider(
            'cobalt',
            new CobaltDebugConfigurationProvider()
        )
    );

	context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory(
        'cobalt',
        new CobaltDebugAdapterFactory()
    ));
}

export function deactivate(): Thenable<void> | undefined {
    return undefined;
}

function launchDebugger(resource: vscode.Uri, attach: boolean) {
    if (resource == null && vscode.window.activeTextEditor != null)
        resource = vscode.window.activeTextEditor.document.uri;
    if (resource == null)
        return;
    
    vscode.debug.startDebugging(
        vscode.workspace.getWorkspaceFolder(resource),
        {
            type: 'cobalt',
            name: 'Debug',
            request: 'launch',
            entryCobPath: resource.fsPath,
            commandLine: []
        },
        {
            noDebug: !attach
        }
    );
}

class CobaltDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
	resolveDebugConfiguration(folder: WorkspaceFolder | undefined, config: DebugConfiguration, token?: CancellationToken): ProviderResult<DebugConfiguration> {
		if (config.type == null
        &&  config.request == null
        &&  config.name == null) {
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

class CobaltDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
    private server?: process.ChildProcess;
    private stdout?: vscode.OutputChannel;

	async createDebugAdapterDescriptor(_session: vscode.DebugSession): Promise<ProviderResult<vscode.DebugAdapterDescriptor>> {
        const config = vscode.workspace.getConfiguration("cobalt");
        const serverDirectory = config.get<string>("toolchainDirectory");
        if (serverDirectory == null) {
            return vscode.window.showErrorMessage("Cobalt Toolchain Directory not specified")
                .then(_ => undefined);
        }

        const serverPath = path.join(serverDirectory, 'Compiler.exe');

        //const pipeName = "eb925160-12d5-485a-8b99-888865cb4ac3"; // For debugging
        const pipeName = crypto.randomUUID();
        const pipePath = path.join('\\\\.\\pipe\\', pipeName);

        this.server = process.spawn(serverPath, [ _session.configuration.entryCobPath, "--debug=" + pipeName, ..._session.configuration.commandLine ], {
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
