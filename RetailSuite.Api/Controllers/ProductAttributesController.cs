// This controller was a stale earlier version of AttributesController and has
// been removed. Its [Route("api/attributes")] with [HttpGet], [HttpPost], and
// [HttpPost("{attributeId}/values")] collided with the current
// AttributesController on the same paths, which caused Swashbuckle to throw
// a 500 when generating /swagger/v1/swagger.json.
//
// TODO: `git rm` this file. It is intentionally empty until then.
