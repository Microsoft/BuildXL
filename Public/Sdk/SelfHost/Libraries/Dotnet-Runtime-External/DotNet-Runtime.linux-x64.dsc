// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {createPublicDotNetRuntime} from "DotNet-Runtime.Common";

const v3 = importFrom("DotNet-Runtime.linux-x64.3.1.100").extracted;

@@public
export const extracted = createPublicDotNetRuntime(v3, undefined);