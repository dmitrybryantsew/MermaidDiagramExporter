const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const folderPath = args[0];
let options = {};
try {
    options = JSON.parse(args[1] || '{}');
} catch (e) {}

let ts;
try {
    // Try resolving in target project first
    const projectTsPath = require.resolve('typescript', { paths: [folderPath] });
    ts = require(projectTsPath);
} catch (e) {
    try {
        // Fallback to local node_modules
        ts = require('typescript');
    } catch (e2) {
        console.error("TypeScript compiler API is not found. Please run 'npm install typescript' in your project or globally.");
        process.exit(1);
    }
}

function findFiles(dir, exts, fileList = []) {
    const files = fs.readdirSync(dir);
    for (const file of files) {
        const fullPath = path.join(dir, file);
        if (fs.statSync(fullPath).isDirectory()) {
            if (file !== 'node_modules' && file !== 'dist' && file !== 'build') {
                findFiles(fullPath, exts, fileList);
            }
        } else if (exts.includes(path.extname(fullPath))) {
            fileList.push(fullPath);
        }
    }
    return fileList;
}

const filePaths = findFiles(folderPath, ['.ts', '.tsx']);

const program = ts.createProgram(filePaths, {
    target: ts.ScriptTarget.ES2020,
    module: ts.ModuleKind.CommonJS,
    allowJs: false
});

const checker = program.getTypeChecker();

const nodes = [];
const edges = [];
const groups = [];

const TypeNodeKind = {
    Class: 'Class',
    Interface: 'Interface',
    Enum: 'Enum',
    Struct: 'Struct'
};

const TypeMemberKind = {
    Field: 'Field',
    Property: 'Property',
    Method: 'Method'
};

const TypeVisibility = {
    Public: 'Public',
    Protected: 'Protected',
    Private: 'Private'
};

const TypeEdgeKind = {
    Inheritance: 'Inheritance',
    Implements: 'Implements',
    Association: 'Association'
};

const TypeGroupKind = {
    Namespace: 'Namespace',
    Folder: 'Folder'
};

const edgeSet = new Set();
function addEdge(from, to, kind, label = '', isStrong = false) {
    if (!from || !to || from === to) return;
    const key = `${from}|${to}|${kind}|${label}`;
    if (!edgeSet.has(key)) {
        edgeSet.add(key);
        edges.push({
            fromNodeId: from,
            toNodeId: to,
            kind,
            label,
            isStrongRelation: isStrong
        });
    }
}

// Stable, unique node id from symbol + file.
// Uses the full hex of the file path (not a truncated 8 chars, which collides
// across files) plus a per-(file,name) counter so duplicate declarations of the
// same name in the same file get distinct ids.
const idCounter = new Map();
const symbolToId = new Map();
function makeId(name, sourceFile, currentNamespace = '') {
    const nsPrefix = currentNamespace ? currentNamespace + '_' : '';
    const fileHex = Buffer.from(sourceFile.fileName).toString('hex');
    const base = `T_${nsPrefix}${name}_${fileHex}`;
    const count = (idCounter.get(base) || 0) + 1;
    idCounter.set(base, count);
    return count > 1 ? `${base}#${count}` : base;
}

function getVisibility(node) {
    if (!node.modifiers) return TypeVisibility.Public;
    for (const mod of node.modifiers) {
        if (mod.kind === ts.SyntaxKind.PrivateKeyword) return TypeVisibility.Private;
        if (mod.kind === ts.SyntaxKind.ProtectedKeyword) return TypeVisibility.Protected;
    }
    return TypeVisibility.Public;
}

function visit(node, sourceFile, currentNamespace = '') {
    if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node) || ts.isEnumDeclaration(node)) {
        const symbol = node.name ? checker.getSymbolAtLocation(node.name) : null;
        if (symbol) {
            const name = symbol.getName();
            const id = makeId(name, sourceFile, currentNamespace);

            let kind = TypeNodeKind.Class;
            if (ts.isInterfaceDeclaration(node)) kind = TypeNodeKind.Interface;
            else if (ts.isTypeAliasDeclaration(node)) kind = TypeNodeKind.Struct;
            else if (ts.isEnumDeclaration(node)) kind = TypeNodeKind.Enum;

            const members = [];
            const type = checker.getTypeAtLocation(node);

            // Base classes / Interfaces — deferred until after the full visit
            // so the target symbol's id is known (resolved via symbolToId).
            if (node.heritageClauses) {
                for (const clause of node.heritageClauses) {
                    const isImplements = clause.token === ts.SyntaxKind.ImplementsKeyword;
                    for (const typeNode of clause.types) {
                        const targetType = checker.getTypeAtLocation(typeNode);
                        const targetSymbol = targetType.aliasSymbol || targetType.symbol;
                        if (targetSymbol) {
                            pendingHeritage.push({
                                from: id,
                                symbol: targetSymbol,
                                kind: isImplements ? TypeEdgeKind.Implements : TypeEdgeKind.Inheritance,
                                label: isImplements ? 'implements' : ''
                            });
                        }
                    }
                }
            }

            if (ts.isEnumDeclaration(node)) {
                // Enum members
                for (const member of node.members) {
                    members.push({
                        name: member.name.getText(),
                        typeName: 'int', // Defaulting to int for enums
                        kind: TypeMemberKind.Field,
                        visibility: TypeVisibility.Public,
                        isStatic: true,
                        isAbstract: false,
                        parameters: []
                    });
                }
            } else if (node.members) {
                for (const member of node.members) {
                    const visibility = getVisibility(member);
                    const isStatic = member.modifiers?.some(m => m.kind === ts.SyntaxKind.StaticKeyword) || false;
                    const isAbstract = member.modifiers?.some(m => m.kind === ts.SyntaxKind.AbstractKeyword) || false;

                    if (ts.isPropertyDeclaration(member) || ts.isPropertySignature(member)) {
                        const memberType = checker.getTypeAtLocation(member);
                        const typeName = checker.typeToString(memberType);
                        members.push({
                            name: member.name.getText(),
                            typeName,
                            kind: TypeMemberKind.Property,
                            visibility,
                            isStatic,
                            isAbstract,
                            parameters: []
                        });

                        // Extract association
                        if (options.includeAssociations !== false) {
                            extractAssociations(memberType, id);
                        }

                    } else if (ts.isMethodDeclaration(member) || ts.isMethodSignature(member)) {
                        const signature = checker.getSignatureFromDeclaration(member);
                        const returnType = signature ? checker.getReturnTypeOfSignature(signature) : checker.getTypeAtLocation(member);
                        const typeName = checker.typeToString(returnType);

                        const parameters = [];
                        for (const param of member.parameters) {
                            const paramType = checker.getTypeAtLocation(param);
                            parameters.push({
                                name: param.name.getText(),
                                typeName: checker.typeToString(paramType)
                            });
                        }

                        members.push({
                            name: member.name.getText(),
                            typeName,
                            kind: TypeMemberKind.Method,
                            visibility,
                            isStatic,
                            isAbstract,
                            parameters
                        });
                    }
                }
            } else if (ts.isTypeAliasDeclaration(node) && ts.isTypeLiteralNode(node.type)) {
                // Handle type aliases that map to object literals
                for (const member of node.type.members) {
                     if (ts.isPropertySignature(member)) {
                        const memberType = checker.getTypeAtLocation(member);
                        const typeName = checker.typeToString(memberType);
                        members.push({
                            name: member.name.getText(),
                            typeName,
                            kind: TypeMemberKind.Property,
                            visibility: TypeVisibility.Public,
                            isStatic: false,
                            isAbstract: false,
                            parameters: []
                        });

                        if (options.includeAssociations !== false) {
                            extractAssociations(memberType, id);
                        }
                     }
                }
            }

            nodes.push({
                id,
                displayName: name,
                fullName: currentNamespace ? `${currentNamespace}.${name}` : name, // We could prepend namespace/module if we tracked it
                namespace: currentNamespace, // Can be extracted from ModuleDeclaration
                assemblyName: '',
                assetPath: sourceFile.fileName,
                kind,
                isProjectType: true,
                stereotypes: [],
                members
            });

            // Link node by symbol for association resolution
            if (!nodeBySymbolMap.has(symbol)) {
                nodeBySymbolMap.set(symbol, id);
            }
        }
    } else if (ts.isModuleDeclaration(node)) {
        // Track namespaces
        let nsName = node.name.text;
        if (currentNamespace) {
            nsName = `${currentNamespace}.${nsName}`;
        }

        // Modules usually have bodies we can visit
        if (node.body) {
            ts.forEachChild(node.body, child => visit(child, sourceFile, nsName));
        }
        return;
    }

    ts.forEachChild(node, child => visit(child, sourceFile, currentNamespace));
}

const nodeBySymbolMap = new Map();

function extractAssociations(type, fromId) {
    if (type.isUnion()) {
        for (const t of type.types) {
            extractAssociations(t, fromId);
        }
    } else if (type.isIntersection()) {
        for (const t of type.types) {
            extractAssociations(t, fromId);
        }
    } else if (type.symbol) {
        // If it's a generic Array<T>, extract T
        if (type.symbol.name === 'Array' && type.typeArguments) {
            for (const arg of type.typeArguments) {
                extractAssociations(arg, fromId);
            }
        } else {
             // Defer edge creation until we process all files so we have IDs mapped
             pendingAssociations.push({ from: fromId, symbol: type.symbol });
        }
    }
}

const pendingAssociations = [];
const pendingHeritage = [];

for (const sourceFile of program.getSourceFiles()) {
    if (!sourceFile.isDeclarationFile) {
        visit(sourceFile, sourceFile);
    }
}

// Resolve pending heritage edges (inheritance / implements) via symbol -> id map.
for (const h of pendingHeritage) {
    const toId = nodeBySymbolMap.get(h.symbol);
    if (toId && toId !== h.from) {
        addEdge(h.from, toId, h.kind, h.label, true);
    }
}

// Resolve pending associations
for (const assoc of pendingAssociations) {
    const toId = nodeBySymbolMap.get(assoc.symbol);
    if (toId && toId !== assoc.from) {
        addEdge(assoc.from, toId, TypeEdgeKind.Association, '', false);
    }
}

// Groups - put everything in one default namespace for simplicity unless we properly track modules
// We can use the primary group kind to group by folder
if (options.primaryGroupKind === TypeGroupKind.Folder || options.primaryGroupKind === 1) { // 1 is Folder in enum
    const folderGroups = new Map();
    for (const node of nodes) {
        const folder = path.dirname(node.assetPath);
        if (!folderGroups.has(folder)) {
            folderGroups.set(folder, {
                id: `Folder:${folder}`,
                label: path.basename(folder) || 'Root',
                kind: TypeGroupKind.Folder,
                parentGroupId: '',
                nodeIds: []
            });
        }
        folderGroups.get(folder).nodeIds.push(node.id);
    }
    for (const group of folderGroups.values()) {
        if (group.nodeIds.length > 0) {
            groups.push(group);
        }
    }
} else {
    // Namespace (default)
    const nsGroups = new Map();
    for (const node of nodes) {
        const ns = node.namespace || 'global';
        if (!nsGroups.has(ns)) {
            nsGroups.set(ns, {
                id: `Namespace:${ns}`,
                label: ns === 'global' ? 'Global Namespace' : ns,
                kind: TypeGroupKind.Namespace,
                parentGroupId: '',
                nodeIds: []
            });
        }
        nsGroups.get(ns).nodeIds.push(node.id);
    }

    for (const group of nsGroups.values()) {
        if (group.nodeIds.length > 0) {
            groups.push(group);
        }
    }
}


const graph = {
    title: 'TypeScript Project',
    nodes,
    edges,
    groups,
    metadata: {
        generatedAtUtc: new Date().toISOString(),
        sourceDescription: 'Scanned from TypeScript files',
        sourceKind: 2, // Folder
        options: options,
        isDerivedView: false,
        parentGraphTitle: '',
        focusSummary: '',
        seedNodeIds: [],
        focusDepth: 0
    }
};

console.log(JSON.stringify(graph, null, 2));
