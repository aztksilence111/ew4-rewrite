function conditional_damage(ctx)
  if ctx.distance == 2 and ctx.source.terrain == "land" and ctx.target.building == "city" then
    return 1.25
  end
  return 1
end
