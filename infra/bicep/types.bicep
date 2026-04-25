// Shared user-defined types for the FTGO Entra Bicep modules.
// Imported via `import { permissionGrant } from '../types.bicep'`.

@export()
@description('A caller→target appRole consent grant.')
type permissionGrant = {
  @description('Key into the apps map for the calling service principal.')
  caller: string

  @description('Key into the apps map for the target (resource) service principal.')
  target: string

  @description('Key into the roleIds map for the appRole granted.')
  role: string
}

@export()
@description('App registration identifiers exposed by app-registrations module.')
type appIds = {
  appId: string
  spId:  string
}
