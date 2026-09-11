import { areas } from './data'
import type { Area } from '../types/models'

const wait = async <T>(value: T): Promise<T> => Promise.resolve(value)
const copyArea = (area: Area): Area => ({ ...area })

export const mockAreasProvider = {
  list: async () => wait(areas.map(copyArea)),
  async save(input: Omit<Area, 'id'> & { id?: number }) {
    if (input.id) {
      const current = areas.find((area) => area.id === input.id)
      if (!current) return wait(undefined)
      Object.assign(current, input)
      return wait(copyArea(current))
    }
    const created: Area = { ...input, id: Math.max(...areas.map((area) => area.id)) + 1 }
    areas.push(created)
    return wait(copyArea(created))
  },
  async toggleStatus(id: number) {
    const area = areas.find((item) => item.id === id)
    if (!area) return wait(undefined)
    area.status = area.status === 'active' ? 'inactive' : 'active'
    return wait(copyArea(area))
  },
}
