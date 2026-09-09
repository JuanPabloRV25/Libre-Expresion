import { describe, expect, it } from 'vitest'
import { permissions, roles } from '../mocks/data'

const officialPermissionCodes = [
  'areas.view',
  'areas.create',
  'areas.edit',
  'areas.activate',
  'users.view',
  'users.create',
  'users.edit',
  'users.activate',
  'users.assign_roles',
  'users.reset_password',
  'roles.view',
  'roles.create',
  'roles.edit',
  'roles.activate',
  'roles.assign_permissions',
  'permissions.view',
  'audit.view',
] as const

describe('DM-003 Fase 1 permission catalog', () => {
  it('contains exactly the 17 official permission codes', () => {
    expect(permissions.map((permission) => permission.code)).toEqual(officialPermissionCodes)
  })

  it('assigns every official permission to Superadmin', () => {
    const superadmin = roles.find((role) => role.id === 1)

    expect(superadmin?.permissionCodes).toEqual(officialPermissionCodes)
  })

  it('keeps the standard internal role without administrative permissions', () => {
    const standardRole = roles.find((role) => role.id === 3)

    expect(standardRole?.permissionCodes).toEqual([])
  })
})
