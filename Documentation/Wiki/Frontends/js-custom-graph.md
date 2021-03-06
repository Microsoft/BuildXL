# Building beyond well-known monorepo managers

Repositories currently building with supported JS package managers or coordinators ([Rush](https://rushjs.io/), [Yarn](https://yarnpkg.com/) and [Lage](https://github.com/microsoft/lage)) can be directly understood by BuildXL. This is done by virtue of querying the corresponding coordinator for the project-to-project graph. However, for non-monorepo scenarios or repositories building in a custom way, moving to any of the above managers in order to build with BuildXL is not always easy. 

BuildXL provides a customizable JavaScript option to address this scenario. A small adapter can be provided by the user to explain to BuildXL the project-to-project graph and the script commands available to execute.

```typescript
config({
    resolvers: [
        {
            kind: "CustomJavaScript",
            moduleName: "my-repo",
            ...
            customProjectGraph: f`graph.json`
        }
});
```

Using a `CustomJavaScript` resolver, a `customProjectGraph` can be provided. This graph can be either a JSON file or a DScript literal. In the example above we are specifying the graph with a JSON file. This file is expected to follow the same schema `yarn workspaces info` returns (check [here](https://classic.yarnpkg.com/en/docs/cli/workspaces/#toc-yarn-workspaces-info)). For example:

```JSON
// graph.json
{
  "@ms/project-a": {
    "location": "packages/project-a",
    "workspaceDependencies": [],
  },
  "@ms/project-b": {
    "location": "packages/project-b",  
    "workspaceDependencies": [],
  },
  "@ms/project-c": {
    "location": "packages/project-c",
    "workspaceDependencies": [
      "@ms/project-a",
      "@ms/project-b"
    ],
  }
}
```

This graph is defining three projects, where `@ms/project-c` depends on the other two. Alternatively, the graph can be generated by an equivalent DScript expression with type `Map<string, {location: RelativePath, workspaceDependencies: string[]}>`. This expression can be inlined in the main config file, but for the sake of clarity the recommendation is to define it in a separate file:

```typescript
config({
    resolvers: [
        {
            kind: "CustomJavaScript",
            moduleName: "my-repo",
            ...
            customProjectGraph: importFile(f`custom-definition.dsc`).getGraph()
        }
});
```

```typescript
// custom-definition.dsc
export function getGraph() : Map<string, {location: RelativePath, workspaceDependencies: string[]}> {
    return Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>()
        .add("@ms/project-a", {location: r`packages/project-a`, workspaceDependencies: []})
        .add("@ms/project-b", {location: r`packages/project-b`, workspaceDependencies: []})
        .add("@ms/project-c", {location: r`packages/project-c`, workspaceDependencies: [
            "@ms/project-a", "@ms/project-b"
        ]})
 }
```
This is a pretty simplistic example, but under `getGraph()` arbitrary DScript code can drive the graph creation, involving querying the file system to understand the shape of the repository, etc.

In the case where corresponding `package.json` files are under each of the specified locations, the 'scripts' section found on those files will be used to define the available script commands for each defined package in the graph. BuildXL will only look at the 'scripts' section, and other aspects of the `package.json` file will be ignored.

Otherwise, there is no need to have physical `package.json` files around. Check [here](js-custom-scripts.md) to see how custom scripts can be specified.

