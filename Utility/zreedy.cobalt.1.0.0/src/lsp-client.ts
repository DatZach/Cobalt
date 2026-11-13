import * as path from 'path';
import * as vscode from 'vscode';

import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;

let serverDirectory: string;

export async function activate(context: vscode.ExtensionContext) {
    outputChannel = outputChannel || vscode.window.createOutputChannel("Cobalt Language Server");

    const config = vscode.workspace.getConfiguration("cobalt");
    serverDirectory = config.get("toolchainDirectory") || "X:\\Cobalt\\Compiler\\bin\\Debug\\net6.0";

    const serverPath = path.join(serverDirectory, 'Compiler.exe');
    const serverOptions: ServerOptions = {
        command: serverPath,
        transport: TransportKind.stdio,
        args: ["--server"],
        options: {
            cwd: serverDirectory
        }
    };

    const clientOptions: LanguageClientOptions = {
        outputChannel: outputChannel,
        documentSelector: [{ scheme: 'file', language: 'cob' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/.clientrc'),
            configurationSection: 'cobalt'
        }
    };
    
    client = new LanguageClient(
        'cobServer',
        'Cobalt Language Server',
        serverOptions,
        clientOptions
    );

    client.start();

    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    if (!client)
		return undefined;

	return client.stop();
}
